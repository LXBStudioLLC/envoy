using Envoy.Core.Models;
using Envoy.Core.Services;
using Xunit;

namespace Envoy.Core.Tests;

/// <summary>APP-09: the answer bank (JSON file store) + the fuzzy matcher bar.</summary>
public sealed class AnswerBankTests : IDisposable
{
    private readonly string _path;

    public AnswerBankTests()
    {
        _path = Path.Combine(Path.GetTempPath(), "envoy-answerbank-tests", Guid.NewGuid().ToString("N") + ".json");
    }

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(_path)!, true); } catch { }
    }

    private AnswerBankRepository Repo() => new(_path);

    [Fact]
    public async Task AddUpdate_List_Persist()
    {
        var repo = Repo();
        await repo.AddOrUpdateAsync("Why do you want to work here?", "Honestly, the mission.", ct: CancellationToken.None);
        await repo.AddOrUpdateAsync("Why do you want to work here?", "Because the mission aligns with my experience.", ct: CancellationToken.None);
        await repo.AddOrUpdateAsync("Relocate?", "Yes, immediately.");

        var all = await repo.GetAllAsync();
        Assert.Equal(2, all.Count);

        var why = all.Single(e => e.Question.Contains("Why", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("aligns", why.Answer); // updated, not duplicated

        // Re-load from disk (fresh repository instance, same file)
        var fresh = Repo();
        var persisted = await fresh.GetAllAsync();
        Assert.Equal(2, persisted.Count);
        Assert.Contains("aligns", persisted.Single(e => e.Question.Contains("Why", StringComparison.OrdinalIgnoreCase)).Answer);
    }

    [Fact]
    public void FuzzyMatcher_ExactBeatsContains()
    {
        var bank = new[]
        {
            new AnswerBankEntry(Guid.NewGuid(), "Why do you want to work here?", "mission", DateTime.UtcNow, DateTime.UtcNow),
            new AnswerBankEntry(Guid.NewGuid(), "Why do you want us to hire you?", "skills", DateTime.UtcNow, DateTime.UtcNow)
        };

        var hit = AnswerBankMatcher.FindBest(bank, "Why do you want to work here?", out var kind);
        Assert.NotNull(hit);
        Assert.Equal("mission", hit!.Answer);
        Assert.Equal("exact", kind.Kind);
    }

    [Fact]
    public void FuzzyMatcher_CloseVariant_Matches()
    {
        var bank = new List<AnswerBankEntry>
        {
            new(Guid.NewGuid(), "Tell us about the most challenging project", "The payments rewrite.", DateTime.UtcNow, DateTime.UtcNow)
        };

        var hit = AnswerBankMatcher.FindBest(bank, "Tell us about the most challenging project you worked on", out _);
        Assert.NotNull(hit);
        Assert.Contains("payments", hit!.Answer);
    }

    [Fact]
    public void FuzzyMatcher_UnrelatedQuestion_DoesNotMatch()
    {
        var bank = new List<AnswerBankEntry>
        {
            new(Guid.NewGuid(), "Why do you want to work here?", "mission", DateTime.UtcNow, DateTime.UtcNow)
        };

        var hit = AnswerBankMatcher.FindBest(bank, "What is your greatest weakness?", out _);
        Assert.Null(hit);
    }

    [Fact]
    public void FuzzyMatcher_DifferentTopicSimilarChars_DoesNotMatch()
    {
        // Same length, different words: similarity bar catches it.
        var bank = new List<AnswerBankEntry>
        {
            new(Guid.NewGuid(), "Why do you want to work here?", "mission", DateTime.UtcNow, DateTime.UtcNow)
        };

        var hit = AnswerBankMatcher.FindBest(bank, "When can you start working with us?", out _);
        Assert.Null(hit);
    }
}
