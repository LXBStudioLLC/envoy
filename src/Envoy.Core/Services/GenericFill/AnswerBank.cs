using System.Text.Json;

namespace Envoy.Core.Services;

/// <summary>
/// One stored answer to a knockout/custom question (APP-09/APP-10). Stored
/// per-question; the question text is the fuzzy lookup key across sites.
/// </summary>
public sealed record AnswerBankEntry(
    Guid Id,
    string Question,
    string Answer,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>Storage for the answer bank. getUser-visible data; stored locally.</summary>
public interface IAnswerBankRepository
{
    Task<List<AnswerBankEntry>> GetAllAsync(CancellationToken ct = default);
    Task AddOrUpdateAsync(string question, string answer, CancellationToken ct = default);
    Task RemoveAsync(Guid id, CancellationToken ct = default);
}

/// <summary>
/// JSON-file-backed answer bank (%LOCALAPPDATA%\Envoy\answers.json), atomic
/// writes, tolerates corrupt content by starting empty. SQLite comes if/when
/// the bank grows; a small JSON file is the honest size for a personal Q&A
/// list and avoids another schema migration (APP-10 owns the UI).
/// </summary>
public sealed class AnswerBankRepository : IAnswerBankRepository
{
    private readonly string _path;
    private List<AnswerBankEntry> _cache = new();
    private bool _loaded;

    public AnswerBankRepository(string path)
    {
        _path = path;
    }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Envoy", "answers.json");

    public async Task<List<AnswerBankEntry>> GetAllAsync(CancellationToken ct = default)
    {
        await LoadAsync(ct);
        return _cache.ToList();
    }

    public async Task AddOrUpdateAsync(string question, string answer, CancellationToken ct = default)
    {
        await LoadAsync(ct);

        question = question.Trim();
        answer = answer.TrimEnd();
        var now = DateTime.UtcNow;
        var existing = _cache.FirstOrDefault(e =>
            string.Equals(e.Question.Trim(), question, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            _cache = _cache.Select(e => e.Id == existing.Id
                ? e with { Answer = answer, UpdatedAt = now }
                : e).ToList();
        else
            _cache.Add(new AnswerBankEntry(Guid.NewGuid(), question, answer, now, now));

        await PersistAsync(ct);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        await LoadAsync(ct);
        _cache = _cache.Where(e => e.Id != id).ToList();
        await PersistAsync(ct);
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        if (_loaded) return;
        try
        {
            if (File.Exists(_path))
            {
                var json = await File.ReadAllTextAsync(_path, ct);
                _cache = JsonSerializer.Deserialize<List<AnswerBankEntry>>(json) ?? new List<AnswerBankEntry>();
            }
        }
        catch (JsonException)
        {
            _cache = new List<AnswerBankEntry>(); // corrupt: start clean; the next Save rewrites the file
        }
        _loaded = true;
    }

    private async Task PersistAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var json = JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = true });
        var tmp = _path + ".tmp";
        await File.WriteAllTextAsync(tmp, json, ct);
        File.Move(tmp, _path, overwrite: true);
    }
}

/// <summary>
/// Fuzzy matching of a live form question against the bank (APP-09). Exact
/// match first; then containment and DomScorer-style similarity at a
/// precision bar — because a wrong bank answer on the wrong question is
/// worse than no answer at all, so "close enough" must genuinely mean close.
/// </summary>
public static class AnswerBankMatcher
{
    /// <summary>Similarity required for a non-exact bank hit (0.0–1.0).</summary>
    public const double SimilarityThreshold = 0.82;

    public static AnswerBankEntry? FindBest(
        IReadOnlyList<AnswerBankEntry> bank, string question, out (double Score, string Kind) hit)
    {
        hit = (0, "none");
        if (bank.Count == 0 || string.IsNullOrWhiteSpace(question))
            return null;

        question = Normalize(question);

        foreach (var entry in bank)
        {
            var candidate = Normalize(entry.Question);
            if (candidate.Length == 0) continue;

            if (candidate == question)
            {
                hit = (1.0, "exact");
                return entry;
            }

            // The live form question is very often the stored question with
            // extra wording at the END ("...you worked on"). A probe that
            // STARTS with a decently long stored question is the same ask.
            if (candidate.Length >= 10 && question.StartsWith(candidate, StringComparison.Ordinal))
            {
                hit = ( (double)candidate.Length / question.Length, "prefix");
                return entry;
            }

            // Containment either way at 0.9+ is a strong signal too.
            if (question.Contains(candidate, StringComparison.OrdinalIgnoreCase) && candidate.Length >= 8)
            {
                var contained = (double)candidate.Length / question.Length;
                if (contained > hit.Score) { hit = (contained, "contained"); }
                if (contained >= 0.85)
                {
                    return entry;
                }
            }

            var sim = DomScorer.NormalizedSimilarity(candidate, question);
            if (sim > hit.Score) { hit = (sim, "similar"); }
            if (sim >= SimilarityThreshold)
            {
                return entry;
            }
        }

        return null;
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var sb = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value.ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
