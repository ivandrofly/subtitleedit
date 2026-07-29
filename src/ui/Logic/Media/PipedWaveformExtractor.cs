using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Logic.Config;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Nikse.SubtitleEdit.Logic.Media;

/// <summary>
/// Waveform peak / spectrogram extraction that streams raw PCM from ffmpeg's stdout pipe
/// instead of transcoding to a temporary WAV file first (see <see cref="WaveFileExtractor"/>).
/// Peaks are computed while ffmpeg is still transcoding, and no multi-hundred-MB temp file
/// is written. Raw s16le is used rather than "-f wav" because a WAV header needs a seek-back
/// to patch the size fields, which a pipe cannot do - and the format is already known here
/// since we choose it on the command line.
/// </summary>
public static class PipedWaveformExtractor
{
    // Same output format the temp-WAV path used: 24 kHz, boosted volume, stereo
    // (or mono when only the center channel is extracted).
    public const int SampleRate = 24000;

    public class PipedWaveformResult
    {
        public required WavePeakData2 Peaks { get; init; }
        public SpectrogramData2? Spectrogram { get; init; }
    }

    /// <summary>
    /// Runs ffmpeg with "-f s16le pipe:1", computes wave peaks (and optionally spectrogram
    /// data) from the piped PCM in a single pass, and writes the same cache files the
    /// temp-WAV flow produced. Returns null on cancellation or ffmpeg failure.
    /// Blocking - call from a worker thread.
    /// </summary>
    public static PipedWaveformResult? Extract(string videoFileName, int audioTrackNumber,
        string peakWaveFileName, string? spectrogramFileName, CancellationToken cancellationToken,
        Action? onSpectrogramStart = null)
    {
        var useCenterChannelOnly = Se.Settings.General.FfmpegUseCenterChannelOnly &&
                                   FfmpegMediaInfo.Parse(videoFileName).HasFrontCenterAudio(audioTrackNumber);
        var channels = useCenterChannelOnly ? 1 : 2;
        var blockAlign = 2 * channels; // 16-bit samples

        using var process = GetPcmPipeProcess(videoFileName, audioTrackNumber, useCenterChannelOnly);

        // Keep only the tail of stderr for diagnostics; ffmpeg's progress chatter would
        // otherwise fill the pipe buffer and deadlock the transcode if left unread.
        var stderrTail = new Queue<string>();
        process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data))
            {
                return;
            }

            lock (stderrTail)
            {
                stderrTail.Enqueue(e.Data);
                while (stderrTail.Count > 20)
                {
                    stderrTail.Dequeue();
                }
            }
        };

        process.Start();
        process.BeginErrorReadLine();

        try
        {
            var result = ReadPeaksAndSamples(process.StandardOutput.BaseStream, channels, blockAlign,
                videoFileName, spectrogramFileName != null, cancellationToken, out var peaksPerSecond, out var allSamples);

            if (cancellationToken.IsCancellationRequested)
            {
                KillProcess(process);
                return null;
            }

            process.WaitForExit();
            if (process.ExitCode != 0 || result.Count == 0)
            {
                string tail;
                lock (stderrTail)
                {
                    tail = string.Join(Environment.NewLine, stderrTail);
                }

                Se.LogError($"PipedWaveformExtractor: ffmpeg exited with code {process.ExitCode} for \"{videoFileName}\"" +
                            Environment.NewLine + tail);
                return null;
            }

            if (!string.IsNullOrWhiteSpace(peakWaveFileName))
            {
                using var peakStream = File.Create(peakWaveFileName);
                WavePeakGenerator2.WriteWaveformData(peakStream, peaksPerSecond, result);
            }

            SpectrogramData2? spectrogram = null;
            if (spectrogramFileName != null && allSamples != null)
            {
                onSpectrogramStart?.Invoke();
                spectrogram = MakeSpectrogram(spectrogramFileName, allSamples, cancellationToken);
            }

            return new PipedWaveformResult
            {
                Peaks = new WavePeakData2(peaksPerSecond, result),
                Spectrogram = spectrogram,
            };
        }
        catch (Exception exception)
        {
            KillProcess(process);
            Se.LogError(exception, $"PipedWaveformExtractor failed for \"{videoFileName}\"");
            return null;
        }
    }

    // Mirrors WaveFileExtractor's ffmpeg arguments (24 kHz, volume boost, track -map,
    // center-channel pan) but sends raw PCM to stdout instead of a WAV file to disk.
    private static Process GetPcmPipeProcess(string inputVideoFile, int audioTrackNumber, bool useCenterChannelOnly)
    {
        var audioParameter = audioTrackNumber >= 0 ? $"-map 0:{audioTrackNumber}? " : string.Empty;

        // ffmpeg only applies the last -af, so pan + volume must live in one filter chain.
        var channelParameter = useCenterChannelOnly
            ? "-af \"pan=mono|c0=FC,volume=1.75\" "
            : "-ac 2 -af volume=1.75 ";

        var arguments = $"-nostdin -i \"{inputVideoFile}\" -vn -ar {SampleRate} {channelParameter}" +
                        $"{audioParameter}-f s16le pipe:1";

        return new Process
        {
            StartInfo = new ProcessStartInfo(GetFfmpegPath(), arguments)
            {
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }
        };
    }

    private static string GetFfmpegPath()
    {
        var exeFilePath = Se.Settings.General.FfmpegPath;
        if (File.Exists(exeFilePath))
        {
            return exeFilePath;
        }

        if (Configuration.IsRunningOnMac && File.Exists("/usr/local/bin/ffmpeg"))
        {
            return "/usr/local/bin/ffmpeg";
        }

        return "ffmpeg"; // resolve via PATH
    }

    private static List<WavePeak2> ReadPeaksAndSamples(Stream pcmStream, int channels, int blockAlign,
        string videoFileName, bool collectSamples, CancellationToken cancellationToken,
        out int peaksPerSecond, out List<float>? allSamples)
    {
        peaksPerSecond = Math.Min(Se.Settings.Waveform.WaveformMinimumSampleRate, SampleRate);

        // ensure that peaks per second is a factor of the sample rate
        while (SampleRate % peaksPerSecond != 0)
        {
            peaksPerSecond++;
        }

        var chunkFrameCount = SampleRate / peaksPerSecond;

        // Match WavePeakGenerator2.GetSampleAndChannelScale: 1 / 2^(bits-1) / channels.
        var sampleAndChannelScale = (float)(1.0 / 32768.0 / channels);

        allSamples = null;
        if (collectSamples)
        {
            // Pre-size from the media duration when known; a short estimate only costs a
            // few list growths, but a good one avoids re-allocating a very large buffer.
            var estimatedFrames = 16 * 1024 * 1024 / sizeof(float);
            var duration = FfmpegMediaInfo2.Parse(videoFileName).Duration;
            if (duration != null && duration.TotalSeconds > 0)
            {
                estimatedFrames = (int)Math.Min(int.MaxValue - 1024L, (long)((duration.TotalSeconds + 2) * SampleRate));
            }

            allSamples = new List<float>(estimatedFrames);
        }

        var peaks = new List<WavePeak2>();
        var buffer = new byte[chunkFrameCount * blockAlign];
        var chunkSamples = new float[chunkFrameCount * 2];

        while (!cancellationToken.IsCancellationRequested)
        {
            var read = ReadFully(pcmStream, buffer);
            if (read < blockAlign)
            {
                break; // end of stream (a trailing partial frame is dropped)
            }

            var frames = read / blockAlign;
            var shorts = MemoryMarshal.Cast<byte, short>(buffer.AsSpan(0, frames * blockAlign));
            var chunkSampleOffset = 0;
            var sIdx = 0;
            while (sIdx < shorts.Length)
            {
                float pos = 0, neg = 0, mixed = 0;
                for (var iChannel = 0; iChannel < channels; iChannel++)
                {
                    var v = shorts[sIdx++];
                    if (v < 0)
                    {
                        neg += v;
                    }
                    else
                    {
                        pos += v;
                    }

                    mixed += v;
                }

                chunkSamples[chunkSampleOffset++] = neg * sampleAndChannelScale;
                chunkSamples[chunkSampleOffset++] = pos * sampleAndChannelScale;
                allSamples?.Add(mixed * sampleAndChannelScale);
            }

            peaks.Add(CalculatePeak(chunkSamples, frames * 2));
        }

        return peaks;
    }

    // Same peak folding as WavePeakGenerator2.CalculatePeak.
    private static WavePeak2 CalculatePeak(float[] chunk, int count)
    {
        if (count == 0)
        {
            return new WavePeak2();
        }

        var max = chunk[0];
        var min = chunk[0];
        for (var i = 1; i < count; i++)
        {
            var value = chunk[i];
            max = Math.Max(max, value);
            min = Math.Min(min, value);
        }

        return new WavePeak2((short)(short.MaxValue * max), (short)(short.MaxValue * min));
    }

    private static SpectrogramData2? MakeSpectrogram(string spectrogramFileName, List<float> allSamples,
        CancellationToken cancellationToken)
    {
        // Same chunk geometry as WavePeakGenerator2.GenerateSpectrogram; the last chunk is
        // zero-padded because the drawer works on whole fftSize*imageWidth blocks.
        const int fftSize = 256;
        const int imageWidth = 1024;
        const int chunkSampleCount = fftSize * imageWidth;

        var chunkCount = (allSamples.Count + chunkSampleCount - 1) / chunkSampleCount;
        var samples = new float[chunkCount * chunkSampleCount];
        allSamples.CopyTo(samples);

        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        var sampleDuration = (double)fftSize / SampleRate;
        SpectrogramData2.SaveToBinaryFile(spectrogramFileName, fftSize, imageWidth, sampleDuration, samples);

        var spectrogram = new SpectrogramData2(fftSize, imageWidth, sampleDuration, samples);
        spectrogram.Load(); // generate images now, for immediate display
        return spectrogram;
    }

    private static int ReadFully(Stream stream, byte[] buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer, total, buffer.Length - total);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private static void KillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // best effort
        }
    }
}
