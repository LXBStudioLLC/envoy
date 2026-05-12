using Envoy.Core.Models;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using System.Text;
using System.Text.Json;

namespace Envoy.Core.Services;

public class ResumeParserService
{
    private readonly OllamaService _ollama;
    private readonly SafetyService _safety;
    private readonly IOcrService? _ocr;
    private readonly ILogger<ResumeParserService> _log;

    public ResumeParserService(OllamaService ollama, SafetyService safety, IOcrService? ocr = null, ILogger<ResumeParserService>? log = null)
    {
        _ollama = ollama;
        _safety = safety;
        _ocr = ocr;
        _log = log ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ResumeParserService>.Instance;
    }

    public async Task<MasterProfile> ParseAsync(string pdfPath, CancellationToken ct = default)
    {
        var rawText = ExtractRawText(pdfPath);
        var ocrUnsupported = false;

        if (rawText.Length < 100 && _ocr != null)
        {
            _log.LogInformation("PDF text too short ({Length} chars), attempting OCR fallback", rawText.Length);
            try
            {
                rawText = await _ocr.ExtractTextFromPdfAsync(pdfPath, ct);
            }
            catch (NotSupportedException ex)
            {
                _log.LogWarning(ex, "OCR fallback not supported for {Path}", pdfPath);
                ocrUnsupported = true;
            }
        }

        var profile = await ReconstructWithLLM(rawText, ct);

        profile.ParseConfidence = CalculateConfidence(profile, rawText);
        profile.Anomalies = DetectAnomalies(profile);

        if (ocrUnsupported)
        {
            profile.Anomalies.Add(new ParseAnomaly
            {
                Field = "Document",
                Message = "PDF appears to be scanned or image-based. Text extraction was minimal and OCR is not yet supported. Edit the profile in the Vault to fill in details manually.",
                Severity = AnomalySeverity.Critical
            });
        }

        return profile;
    }

    private string ExtractRawText(string pdfPath)
    {
        var sb = new StringBuilder();
        try
        {
            using var document = PdfDocument.Open(pdfPath);
            foreach (var page in document.GetPages())
            {
                sb.AppendLine(page.Text);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to extract text from PDF: {Path}", pdfPath);
        }
        return sb.ToString();
    }

    private async Task<MasterProfile> ReconstructWithLLM(string rawText, CancellationToken ct)
    {
        var systemPrompt = @"You are an expert Resume Parser. Your task is to reconstruct raw extracted text from a PDF into structured JSON.

Rules:
1. Identify Job Titles, Companies, Dates, and Bullet Points accurately.
2. Do not invent or embellish any information. Only reorganize what is present.
3. Dates should be in format: Month Year (e.g., Jan 2020).
4. Output ONLY valid JSON matching this schema exactly.
5. If a field is unclear or missing, use empty string or empty array.

JSON Schema:
{
  ""Name"": """",
  ""Email"": """",
  ""Phone"": """",
  ""Location"": """",
  ""LinkedIn"": """",
  ""Website"": """",
  ""Summary"": """",
  ""Skills"": [],
  ""Experience"": [
    {
      ""JobTitle"": """",
      ""Company"": """",
      ""Location"": """",
      ""StartDate"": """",
      ""EndDate"": """",
      ""IsCurrent"": false,
      ""Bullets"": []
    }
  ],
  ""Education"": [
    {
      ""Degree"": """",
      ""Institution"": """",
      ""GraduationDate"": """",
      ""Location"": """"
    }
  ],
  ""Projects"": [
    {
      ""Name"": """",
      ""Description"": """",
      ""Technologies"": []
    }
  ]
}";

        var truncatedText = TruncateAtSentenceBoundary(rawText, 8000);
        var prompt = $"Parse the following resume text into the JSON schema. Raw text:\n\n{truncatedText}";

        var result = await _ollama.CompleteJsonAsync<MasterProfile>(prompt, systemPrompt, ct);

        if (result == null || string.IsNullOrWhiteSpace(result.Name))
        {
            _log.LogWarning("LLM returned null or empty profile. Returning minimal profile.");
            return new MasterProfile { ParseConfidence = 0 };
        }

        return result;
    }

    private static string TruncateAtSentenceBoundary(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;

        var truncated = text[..maxLength];
        var lastSentenceEnd = truncated.LastIndexOfAny(new[] { '.', '!', '?', '\n' });
        if (lastSentenceEnd > maxLength * 0.5)
            return truncated[..(lastSentenceEnd + 1)].Trim();

        var lastSpace = truncated.LastIndexOf(' ');
        if (lastSpace > 0)
            return truncated[..lastSpace].Trim() + "...";

        return truncated + "...";
    }

    private double CalculateConfidence(MasterProfile profile, string rawText)
    {
        var score = 1.0;

        if (string.IsNullOrWhiteSpace(profile.Name)) score -= 0.3;
        if (string.IsNullOrWhiteSpace(profile.Email)) score -= 0.2;
        if (profile.Experience == null || !profile.Experience.Any()) score -= 0.3;
        if (profile.Skills == null || !profile.Skills.Any()) score -= 0.1;

        if (profile.Experience != null)
        {
            foreach (var exp in profile.Experience)
            {
                if (string.IsNullOrWhiteSpace(exp.StartDate)) score -= 0.05;
                if (exp.Bullets == null || !exp.Bullets.Any()) score -= 0.05;
            }
        }

        if (rawText.Length < 100)
            score -= 0.2;

        return Math.Max(0, score);
    }

    private List<ParseAnomaly> DetectAnomalies(MasterProfile profile)
    {
        var anomalies = new List<ParseAnomaly>();

        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            anomalies.Add(new ParseAnomaly
            {
                Field = "Name",
                Message = "Name could not be detected",
                Severity = AnomalySeverity.Critical
            });
        }

        if (profile.Experience != null)
        {
            foreach (var exp in profile.Experience)
            {
                if (string.IsNullOrWhiteSpace(exp.StartDate) && string.IsNullOrWhiteSpace(exp.EndDate))
                {
                    anomalies.Add(new ParseAnomaly
                    {
                        Field = $"Experience.{exp.JobTitle}.Dates",
                        Message = $"No dates found for {exp.JobTitle} at {exp.Company}",
                        Severity = AnomalySeverity.Warning
                    });
                }
            }
        }

        return anomalies;
    }
}