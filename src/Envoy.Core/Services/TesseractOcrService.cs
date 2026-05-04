using System.Diagnostics;

namespace Envoy.Core.Services;

public interface IOcrService
{
    Task<string> ExtractTextAsync(string imagePath, CancellationToken ct = default);
    Task<string> ExtractTextFromPdfAsync(string pdfPath, CancellationToken ct = default);
}

public class TesseractOcrService : IOcrService
{
    private readonly string? _tesseractPath;

    public TesseractOcrService(string? tesseractPath = null)
    {
        _tesseractPath = tesseractPath ?? FindTesseract();
    }

    public async Task<string> ExtractTextAsync(string imagePath, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_tesseractPath))
        {
            throw new InvalidOperationException("Tesseract not found. Please install Tesseract OCR.");
        }

        var tempOutput = Path.Combine(Path.GetTempPath(), $"tesseract_{Guid.NewGuid()}");
        var psi = new ProcessStartInfo
        {
            FileName = _tesseractPath,
            Arguments = $"\"{imagePath}\" \"{tempOutput}\" -l eng",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start Tesseract process.");
        }

        await process.WaitForExitAsync(ct);

        var outputFile = tempOutput + ".txt";
        if (File.Exists(outputFile))
        {
            var text = await File.ReadAllTextAsync(outputFile, ct);
            try { File.Delete(outputFile); } catch { }
            return text;
        }

        return string.Empty;
    }

    public Task<string> ExtractTextFromPdfAsync(string pdfPath, CancellationToken ct = default)
    {
        return Task.FromResult(string.Empty);
    }

    private static string? FindTesseract()
    {
        var possiblePaths = new[]
        {
            @"C:\Program Files\Tesseract-OCR\tesseract.exe",
            @"C:\Program Files (x86)\Tesseract-OCR\tesseract.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"scoop\shims\tesseract.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"chocolatey\lib\tesseract\tools\tesseract.exe"),
            "/usr/bin/tesseract",
            "/usr/local/bin/tesseract",
            "/opt/homebrew/bin/tesseract",
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
                return path;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows) ? "where" : "which",
                Arguments = "tesseract",
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            using var process = Process.Start(psi);
            if (process != null)
            {
                var output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
                if (!string.IsNullOrEmpty(output) && File.Exists(output.Split('\n')[0].Trim()))
                    return output.Split('\n')[0].Trim();
            }
        }
        catch { }

        return null;
    }
}