using Envoy.GhostDetection.Models;
using System.Text.Json;

namespace Envoy.Discovery.Internal;

/// <summary>
/// Builds the cross-company comparison corpus that <c>DuplicateJdSignal</c> reads from
/// <c>JobPosting.Extra["dupcheck.corpus"]</c>, using only the postings already fetched in
/// the current discovery batch — sanctioned public data already in hand, no extra requests.
/// The same JSON string is shared by reference across the batch, so the cost is O(corpus),
/// not O(batch x corpus).
/// </summary>
internal static class DuplicateJdCorpus
{
    // Key and thresholds mirror DuplicateJdSignal (min comparable length, max entries it reads).
    private const string CorpusKey = "dupcheck.corpus";
    private const int MaxEntries = 50;
    private const int MinChars = 400;
    private const int MaxDescChars = 4000;

    /// <summary>
    /// Attach a shared duplicate-detection corpus to every posting in <paramref name="postings"/>.
    /// No-ops when there are fewer than two postings with enough description text to compare.
    /// </summary>
    public static void Attach(IReadOnlyList<JobPosting> postings)
    {
        if (postings.Count < 2)
            return;

        var entries = new List<CorpusEntry>(Math.Min(postings.Count, MaxEntries));
        foreach (var p in postings)
        {
            if (string.IsNullOrWhiteSpace(p.CompanyName))
                continue;

            var desc = p.DescriptionText;
            if (string.IsNullOrWhiteSpace(desc) || desc.Length < MinChars)
                continue;

            if (desc.Length > MaxDescChars)
                desc = desc[..MaxDescChars];

            entries.Add(new CorpusEntry { Company = p.CompanyName, Description = desc });
            if (entries.Count >= MaxEntries)
                break;
        }

        // Need at least two comparable postings for a cross-company duplicate to be possible.
        if (entries.Count < 2)
            return;

        var json = JsonSerializer.Serialize(entries);
        foreach (var p in postings)
            p.Extra[CorpusKey] = json;
    }

    private sealed class CorpusEntry
    {
        public string Company { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}
