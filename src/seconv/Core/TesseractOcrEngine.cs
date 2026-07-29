using System.Diagnostics;
using SkiaSharp;

namespace SeConv.Core;

/// <summary>
/// OCR engine that delegates to a Tesseract binary on the system PATH. seconv does not
/// bundle Tesseract — users install it separately (e.g. <c>apt install tesseract-ocr</c>,
/// <c>brew install tesseract</c>, or the Windows installer from UB Mannheim).
/// </summary>
internal sealed class TesseractOcrEngine : IOcrEngine
{
    public string Name => "tesseract";
    public string ExecutablePath { get; }
    public string Language { get; }

    private TesseractOcrEngine(string executablePath, string language)
    {
        ExecutablePath = executablePath;
        Language = language;
    }

    /// <summary>
    /// Locates Tesseract on the system PATH (cross-platform). Returns null if missing.
    /// </summary>
    public static string? Detect()
    {
        var name = OperatingSystem.IsWindows() ? "tesseract.exe" : "tesseract";
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var separator = OperatingSystem.IsWindows() ? ';' : ':';
        foreach (var dir in pathEnv.Split(separator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim(), name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    public static TesseractOcrEngine Create(string language = "eng")
    {
        var path = Detect()
            ?? throw new InvalidOperationException(
                "Tesseract not found on PATH. Install it from https://tesseract-ocr.github.io/ " +
                "(or `apt install tesseract-ocr` / `brew install tesseract`) and ensure the binary is on PATH.");

        return new TesseractOcrEngine(path, language);
    }

    /// <summary>
    /// Runs Tesseract on a single bitmap and returns the recognised text. The bitmap is
    /// composited onto white (Tesseract handles antialiased text better with an opaque
    /// background) and scaled up 2× for accuracy on small subtitle bitmaps.
    /// </summary>
    public string Recognize(SKBitmap bitmap)
    {
        if (bitmap is null || bitmap.Width == 0 || bitmap.Height == 0)
        {
            return string.Empty;
        }

        var prepped = Preprocess(bitmap);
        try
        {
            // Pipe the PNG to Tesseract's stdin ("stdin" pseudo file name) instead of writing
            // a temp file per image - a full .sup OCR run used to create thousands of them.
            using var image = SKImage.FromBitmap(prepped);
            using var data = image.Encode(SKEncodedImageFormat.Png, 90);

            var psi = new ProcessStartInfo(ExecutablePath)
            {
                ArgumentList = { "stdin", "stdout", "-l", Language, "--psm", "6" },
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start tesseract process.");
            // Drain stderr concurrently — Tesseract emits warnings to stderr, and reading
            // stdout to completion while stderr fills the pipe buffer would deadlock.
            var stderrTask = proc.StandardError.ReadToEndAsync();
            try
            {
                using var stdin = proc.StandardInput.BaseStream;
                data.AsStream().CopyTo(stdin);
            }
            catch (IOException)
            {
                // Tesseract exited before consuming the whole image (e.g. missing language
                // data) - fall through, the exit code / stderr carry the real error.
            }

            var stdout = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();
            if (proc.ExitCode != 0)
            {
                var err = stderrTask.GetAwaiter().GetResult();
                throw new InvalidOperationException($"Tesseract exited with code {proc.ExitCode}: {err}");
            }
            return stdout.Trim();
        }
        finally
        {
            prepped.Dispose();
        }
    }

    private static SKBitmap Preprocess(SKBitmap source)
    {
        const int scale = 2;
        var w = source.Width * scale;
        var h = source.Height * scale;
        var prepped = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(prepped);
        canvas.Clear(SKColors.White);
        using var image = SKImage.FromBitmap(source);
        canvas.DrawImage(image, new SKRect(0, 0, w, h), new SKSamplingOptions(SKCubicResampler.Mitchell));
        canvas.Flush();
        return prepped;
    }

    public void Dispose()
    {
        // Nothing to clean up - images are piped, no temp work folder exists anymore.
    }
}
