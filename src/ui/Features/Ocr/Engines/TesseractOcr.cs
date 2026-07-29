using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Logic;
using Nikse.SubtitleEdit.Logic.Config;
using SkiaSharp;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Nikse.SubtitleEdit.Features.Ocr;

public class TesseractOcr
{
    public string Error { get; set; }

    public TesseractOcr()
    {
        Error = string.Empty;
    }

    private string _executablePath = string.Empty;

    public static string GetExecutablePath()
    {
        if (OperatingSystem.IsWindows())
        {
            var windowsPath = Path.Combine(Se.TesseractFolder, "tesseract.exe");
            return File.Exists(windowsPath) ? windowsPath : "tesseract.exe";
        }

        ReadOnlySpan<string> unixPaths =
        [
            "/opt/homebrew/bin/tesseract",
            "/opt/local/bin/tesseract",
            "/usr/local/bin/tesseract",
            "/usr/bin/tesseract",
            "/snap/bin/tesseract",
            "/opt/tesseract/bin/tesseract",
            "/home/linuxbrew/.linuxbrew/bin/tesseract",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/bin/tesseract"),
            "/app/bin/tesseract"
        ];

        foreach (var path in unixPaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return "tesseract";
    }

    public async Task<string> Ocr(SKBitmap bitmap, string language, string tessDataFolder, CancellationToken cancellationToken, int engineMode = 3)
    {
        if (string.IsNullOrEmpty(_executablePath))
        {
            _executablePath = GetExecutablePath();
        }

        // Preprocess image: binarize to black text on white. Keys on brightness so coloured text
        // (e.g. yellow) is kept rather than blanked the way the blue-only MakeOneColor did.
        var nbmp = new NikseBitmap(bitmap);
        nbmp.MakeBlackAndWhiteForOcr();
        using var oneColorBitmap = nbmp.GetBitmap();

        // When the tools log is on, record what Tesseract is actually fed: how much "ink" survived
        // the black/white preprocessing (0% means the text was blanked, e.g. coloured subtitles) and
        // a copy of the exact image, so blank output can be diagnosed without guessing.
        if (Se.Settings.Tools.WriteToolsLog)
        {
            var inkPercent = GetInkPercent(nbmp);
            Se.WriteToolsLog($"Tesseract OCR: input {oneColorBitmap.Width}x{oneColorBitmap.Height}, ink={inkPercent:0.0}% (0% = preprocessing blanked the text), lang={language}, oem={engineMode}");
            try
            {
                var logDir = Path.GetDirectoryName(Se.GetToolsLogFilePath()) ?? Path.GetTempPath();
                var debugCopy = Path.Combine(logDir, "tesseract-input.png");
                await File.WriteAllBytesAsync(debugCopy, oneColorBitmap.ToPngArray(), cancellationToken);
                Se.WriteToolsLog($"Tesseract OCR: saved preprocessed image to {debugCopy}");
            }
            catch
            {
                // ignore debug-save failures
            }
        }

        // The image is piped to tesseract's stdin and the hOCR comes back on stdout
        // ("stdin"/"stdout" pseudo file names), so a batch OCR run creates no temp files
        // at all - the old flow wrote one .png and one .hocr to disk per image.
        // Use -c inline variables instead of the "hocr" configfile — avoids requiring a
        // configs/ subdirectory in the tessdata folder (user-downloaded packs don't include it).
        var psi = new ProcessStartInfo
        {
            FileName = _executablePath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("stdin");
        psi.ArgumentList.Add("stdout");
        psi.ArgumentList.Add("--tessdata-dir");
        psi.ArgumentList.Add(tessDataFolder);
        psi.ArgumentList.Add("-l");
        psi.ArgumentList.Add(language);
        psi.ArgumentList.Add("--psm");
        psi.ArgumentList.Add("6");
        psi.ArgumentList.Add("--oem");
        psi.ArgumentList.Add(engineMode.ToString(System.Globalization.CultureInfo.InvariantCulture));
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("tessedit_create_hocr=1");
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("tessedit_create_txt=0");

#pragma warning disable CA1416 // Validate platform compatibility
        using var process = new Process { StartInfo = psi };
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Error = $"Could not start Tesseract at \"{_executablePath}\": {ex.Message}." +
                    (OperatingSystem.IsWindows()
                        ? string.Empty
                        : " Make sure Tesseract is installed (e.g. \"brew install tesseract\" on macOS, \"apt install tesseract-ocr\" on Linux).");
            return string.Empty;
        }
#pragma warning restore CA1416 // Validate platform compatibility

        // Start draining stdout and stderr before feeding stdin: tesseract blocked on a full
        // output pipe buffer while we block writing input would deadlock both processes.
        var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

        try
        {
            try
            {
                await process.StandardInput.BaseStream.WriteAsync(oneColorBitmap.ToPngArray(), cancellationToken);
            }
            catch (IOException)
            {
                // tesseract exited before reading all of the image (e.g. bad language/arguments);
                // the exit code and stderr below carry the real error.
            }
            finally
            {
                try
                {
                    process.StandardInput.Close();
                }
                catch
                {
                    // ignore - already closed by a dying process
                }
            }

            await process.WaitForExitAsync(cancellationToken);
        }
        catch
        {
            process.Kill();
            throw;
        }

        var stderr = await stderrTask;
        if (Se.Settings.Tools.WriteToolsLog)
        {
            Se.WriteToolsLog($"Tesseract OCR: exit={process.ExitCode}" +
                             (string.IsNullOrWhiteSpace(stderr) ? string.Empty : " stderr=" + stderr.Trim()));
        }

        if (process.ExitCode != 0)
        {
            Error = string.IsNullOrWhiteSpace(stderr)
                ? $"Tesseract exited with code {process.ExitCode}."
                : stderr.Trim();
            return string.Empty;
        }

        return ParseHOcr(await stdoutTask);
    }

    // Percentage of dark "ink" pixels in the preprocessed (black-on-white) image. ~0% means the
    // black/white conversion blanked the text (e.g. coloured subtitles), which yields empty OCR.
    private static double GetInkPercent(NikseBitmap nbmp)
    {
        long total = (long)nbmp.Width * nbmp.Height;
        if (total == 0)
        {
            return 0;
        }

        long ink = 0;
        for (var y = 0; y < nbmp.Height; y++)
        {
            for (var x = 0; x < nbmp.Width; x++)
            {
                var c = nbmp.GetPixel(x, y);
                if (c.Alpha > 0 && c.Red < 128 && c.Green < 128 && c.Blue < 128)
                {
                    ink++;
                }
            }
        }

        return ink * 100.0 / total;
    }

    private static string ParseHOcr(string html)
    {
        var sb = new StringBuilder();
        var lineStart = html.IndexOf("<span class='ocr_line'", StringComparison.InvariantCulture);
        var alternateLineStart = html.IndexOf("<span class='ocr_header'", StringComparison.InvariantCulture);
        if (alternateLineStart > 0 && (lineStart < 0 || alternateLineStart < lineStart))
        {
            lineStart = alternateLineStart;
        }

        while (lineStart > 0)
        {
            var wordStart = html.IndexOf("<span class='ocrx_word'", lineStart, StringComparison.InvariantCulture);
            var wordMax = html.IndexOf("<span class='ocr_line'", lineStart + 1, StringComparison.InvariantCulture);
            if (wordMax <= 0)
            {
                wordMax = html.Length;
            }

            while (wordStart > 0 && wordStart <= wordMax)
            {
                var startText = html.IndexOf('>', wordStart + 1);
                if (startText > 0)
                {
                    startText++;
                    var endText = html.IndexOf("</span>", startText, StringComparison.InvariantCulture);
                    if (endText > 0)
                    {
                        var text = html.Substring(startText, endText - startText);
                        sb.Append(text.Trim()).Append(' ');
                    }
                }
                wordStart = html.IndexOf("<span class='ocrx_word'", wordStart + 1, StringComparison.InvariantCulture);
            }
            sb.AppendLine();
            lineStart = html.IndexOf("<span class='ocr_line'", lineStart + 1, StringComparison.InvariantCulture);
        }
        sb.Replace("<em>", "<i>")
          .Replace("</em>", "</i>")
          .Replace("<strong>", string.Empty)
          .Replace("</strong>", string.Empty)
          .Replace("</i> <i>", " ")
          .Replace("</i><i>", string.Empty);

        // html escape decoding
        sb.Replace("&amp;", "&")
          .Replace("&lt;", "<")
          .Replace("&gt;", ">")
          .Replace("&quot;", "\"")
          .Replace("&#39;", "'")
          .Replace("&apos;", "'");

        sb.Replace("</i>" + Environment.NewLine + "<i>", Environment.NewLine)
          .Replace(" " + Environment.NewLine, Environment.NewLine);

        return sb.ToString().Trim();
    }
}
