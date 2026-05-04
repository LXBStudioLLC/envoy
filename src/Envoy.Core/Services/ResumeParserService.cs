using Envoy.Core.Models;
using UglyToad.PdfPig;
using System.Text;
using System.Text.Json;

namespace Envoy.Core.Services;

public class ResumeParserService
{
    private readonly OllamaService _ollama;
    private readonly SafetyService _safety;
    private readonly IOcrService? _ocr;

    public ResumeParserService(OllamaService ollama, SafetyService safety, IOcrService? ocr = null)
    {
        _ollama = ollama;
        _safety = safety;
        _ocr = ocr;
    }

    public async Task<MasterProfile> ParseAsync(string pdfPath, CancellationToken ct = default)
    {
        var rawText = ExtractRawText(pdfPath);
        
        // If text extraction returns very little, might be image-based PDF
        if (rawText.Length < 100 && _ocr != null)
        {
            rawText = await _ocr.ExtractTextFromPdfAsync(pdfPath, ct);
        }

        var profile = await ReconstructWithLLM(rawText, ct);

        profile.ParseConfidence = CalculateConfidence(profile, rawText);
        profile.Anomalies = DetectAnomalies(profile);

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
catch
        {
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
  ""Name"": "",
  ""Email"": "",
  ""Phone"": "",
  ""Location"": "",
  ""LinkedIn"": "",
  ""Website"": "",
  ""Summary"": "",
  ""Skills"": [],
  ""Experience"": [
    {
      ""JobTitle"": "",
      ""Company"": "",
      ""Location"": "",
      ""StartDate"": "",
      ""EndDate"": "",
      ""IsCurrent"": false,
      ""Bullets"": []
    }
  ],
  ""Education"": [
    {
      ""Degree"": "",
      ""Institution"": "",
      ""GraduationDate"": "",
      ""Location"": ""
    }
  ],
  ""Projects"": [
    {
      ""Name"": "",
      ""Description"": "",
      ""Technologies"": []
    }
  ]
}";

        var prompt = $"Parse the following resume text into the JSON schema. Raw text:\n\n{rawText[..Math.Min(rawText.Length, 8000)]}";

        var result = await _ollama.CompleteJsonAsync<MasterProfile>(prompt, systemPrompt, ct);
        return result ?? new MasterProfile();
    }

    private double CalculateConfidence(MasterProfile profile, string rawText)
    {
        var score = 1.0;

        if (string.IsNullOrWhiteSpace(profile.Name)) score -= 0.3;
        if (string.IsNullOrWhiteSpace(profile.Email)) score -= 0.2;
        if (!profile.Experience.Any()) score -= 0.3;
        if (!profile.Skills.Any()) score -= 0.1;

        foreach (var exp in profile.Experience)
        {
            if (string.IsNullOrWhiteSpace(exp.StartDate)) score -= 0.05;
            if (!exp.Bullets.Any()) score -= 0.05;
        }

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

        return anomalies;
    }
}
