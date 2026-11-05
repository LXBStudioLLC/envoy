using Envoy.GhostDetection.Models;
using Envoy.GhostDetection.Signals;
using System.Text.Json;
using Xunit;

namespace Envoy.GhostDetection.Tests;

public class DuplicateJdSignalTests
{
    private static readonly DuplicateJdSignal Signal = new();

    // A long, token-rich job description (≥400 chars, ≥60 tokens).
    private static readonly string LongJd =
        "We are seeking a passionate software engineer to join our growing team. " +
        "You will design, develop, and maintain high-quality software applications. " +
        "The ideal candidate has strong problem-solving skills and experience with modern frameworks. " +
        "Responsibilities include writing clean code, performing code reviews, and collaborating with cross-functional teams. " +
        "Requirements: 3+ years of experience, proficiency in C# and .NET, familiarity with cloud platforms. " +
        "Benefits include competitive salary, health insurance, and flexible working hours. " +
        "We value diversity and inclusion and are an equal opportunity employer. " +
        "Apply today to be part of our innovative journey.";

    private static JobPosting BuildPosting(string company, string description, string? corpusJson = null)
    {
        var extra = new Dictionary<string, string>();
        if (corpusJson != null)
            extra["dupcheck.corpus"] = corpusJson;

        return new JobPosting
        {
            CompanyName = company,
            DescriptionText = description,
            Extra = extra
        };
    }

    private static string BuildCorpusJson(params (string Company, string Description)[] entries)
    {
        var list = entries.Select(e => new { company = e.Company, description = e.Description }).ToList();
        return JsonSerializer.Serialize(list);
    }

    [Fact]
    public async Task EvaluateAsync_MissingCorpusKey_ReturnsNull()
    {
        var posting = BuildPosting("Acme Corp", LongJd);
        var result = await Signal.EvaluateAsync(posting);
        Assert.Null(result);
    }

    [Fact]
    public async Task EvaluateAsync_MalformedCorpusJson_ReturnsNull()
    {
        var posting = BuildPosting("Acme Corp", LongJd, corpusJson: "not-valid-json");
        var result = await Signal.EvaluateAsync(posting);
        Assert.Null(result);
    }

    [Fact]
    public async Task EvaluateAsync_PostingBelowSizeGuard_ReturnsNull()
    {
        var shortDesc = "Short job description.";
        var corpus = BuildCorpusJson(("Other Corp", LongJd));
        var posting = BuildPosting("Acme Corp", shortDesc, corpus);
        var result = await Signal.EvaluateAsync(posting);
        Assert.Null(result);
    }

    [Fact]
    public async Task EvaluateAsync_VerbatimDuplicate_UnrelatedCompany_Fires()
    {
        var corpus = BuildCorpusJson(("Other Corp", LongJd));
        var posting = BuildPosting("Acme Corp", LongJd, corpus);

        var result = await Signal.EvaluateAsync(posting);

        Assert.NotNull(result);
        Assert.True(result.Score >= 0.75, $"Expected Score >= 0.75, got {result.Score}");
        Assert.Equal(0.55, result.Confidence);
        Assert.NotEmpty(result.Evidence);
        Assert.Equal(SignalTier.Weak, result.Tier);
    }

    [Fact]
    public async Task EvaluateAsync_SameCompanyExactName_ReturnsNull()
    {
        var corpus = BuildCorpusJson(("Acme Corp", LongJd));
        var posting = BuildPosting("Acme Corp", LongJd, corpus);

        var result = await Signal.EvaluateAsync(posting);

        Assert.Null(result);
    }

    [Fact]
    public async Task EvaluateAsync_SameCompanySimilarName_ReturnsNull()
    {
        // "Acme Corp" vs "Acme Corp." — DomScorer.NormalizedSimilarity >= 0.85
        var corpus = BuildCorpusJson(("Acme Corp.", LongJd));
        var posting = BuildPosting("Acme Corp", LongJd, corpus);

        var result = await Signal.EvaluateAsync(posting);

        Assert.Null(result);
    }

    [Fact]
    public async Task EvaluateAsync_GenuinelyDifferentJd_ReturnsNull()
    {
        // Same vocabulary domain but different 5-gram runs
        var differentJd =
            "Our engineering organization is hiring talented developers. " +
            "You will build scalable systems and contribute to open-source tools. " +
            "The right person has a background in distributed systems and enjoys mentoring. " +
            "Day-to-day work involves architecture decisions, pair programming, and release automation. " +
            "We look for 5+ years of hands-on coding, deep knowledge of Python or Go, and comfort with Kubernetes. " +
            "Perks include unlimited PTO, remote-first culture, and equity participation. " +
            "We believe in sustainable pace and psychological safety for every team member. " +
            "Send us your portfolio and let us build something meaningful together.";

        var corpus = BuildCorpusJson(("Other Corp", differentJd));
        var posting = BuildPosting("Acme Corp", LongJd, corpus);

        var result = await Signal.EvaluateAsync(posting);

        Assert.Null(result);
    }

    [Fact]
    public async Task EvaluateAsync_ThreeUnrelatedMatches_Confidence80()
    {
        var corpus = BuildCorpusJson(
            ("Corp A", LongJd),
            ("Corp B", LongJd),
            ("Corp C", LongJd));
        var posting = BuildPosting("Acme Corp", LongJd, corpus);

        var result = await Signal.EvaluateAsync(posting);

        Assert.NotNull(result);
        Assert.Equal(0.80, result.Confidence);
        Assert.Contains("3 postings", result.Evidence[1]);
    }

    [Fact]
    public async Task EvaluateAsync_PartialCopy_FiresWithSaneScore()
    {
        // Replace ~30% of the text (last 2 sentences) while keeping the first 70%
        var partialCopy =
            "We are seeking a passionate software engineer to join our growing team. " +
            "You will design, develop, and maintain high-quality software applications. " +
            "The ideal candidate has strong problem-solving skills and experience with modern frameworks. " +
            "Responsibilities include writing clean code, performing code reviews, and collaborating with cross-functional teams. " +
            "Requirements: 3+ years of experience, proficiency in C# and .NET, familiarity with cloud platforms. " +
            "Benefits include competitive salary, health insurance, and flexible working hours. " +
            "We are a fast-paced startup looking for self-starters who thrive under pressure. " +
            "Submit your resume and a cover letter explaining why you are the perfect fit.";

        var corpus = BuildCorpusJson(("Other Corp", partialCopy));
        var posting = BuildPosting("Acme Corp", LongJd, corpus);

        var result = await Signal.EvaluateAsync(posting);

        Assert.NotNull(result);
        Assert.True(result.Score >= 0.40 && result.Score <= 0.70,
            $"Expected Score in [0.40, 0.70] for partial copy, got {result.Score}");
    }

    [Fact]
    public async Task EvaluateAsync_CorpusEntryBelowSizeGuard_SkipsQuietly()
    {
        var shortEntry = "Short description.";
        var corpus = BuildCorpusJson(
            ("Other Corp", shortEntry),
            ("Match Corp", LongJd));
        var posting = BuildPosting("Acme Corp", LongJd, corpus);

        var result = await Signal.EvaluateAsync(posting);

        Assert.NotNull(result);
        Assert.Single(result.Evidence);
        Assert.Contains("Match Corp", result.Evidence[0]);
    }

    [Fact]
    public async Task EvaluateAsync_EmptyCorpusArray_ReturnsNull()
    {
        var corpus = "[]";
        var posting = BuildPosting("Acme Corp", LongJd, corpus);
        var result = await Signal.EvaluateAsync(posting);
        Assert.Null(result);
    }

    [Fact]
    public async Task EvaluateAsync_CorpusWithNullFields_SkipsQuietly()
    {
        var corpus = "[{\"company\":null,\"description\":\"some text...\"},{\"company\":\"Other Corp\",\"description\":\"" + LongJd + "\"}]";
        var posting = BuildPosting("Acme Corp", LongJd, corpus);

        var result = await Signal.EvaluateAsync(posting);

        Assert.NotNull(result);
        Assert.Contains("Other Corp", result.Evidence[0]);
    }

    [Fact]
    public async Task EvaluateAsync_FixtureTemplateFarm_FiresWithWeakTier()
    {
        var assemblyDir = Path.GetDirectoryName(typeof(DuplicateJdSignalTests).Assembly.Location)!;
        var fixturePath = Path.Combine(assemblyDir, "..", "..", "..", "fixtures", "posting-dupjd-template-farm.json");
        var raw = File.ReadAllText(fixturePath);
        var stripped = string.Join("\n", raw.Split('\n').Where(l => !l.TrimStart().StartsWith("//")));
        var fixture = JsonSerializer.Deserialize<JobPosting>(stripped)!;
        fixture.Extra["dupcheck.corpus"] = BuildCorpusJson(("Unrelated Corp", fixture.DescriptionText));

        var result = await Signal.EvaluateAsync(fixture);

        Assert.NotNull(result);
        Assert.Equal(SignalTier.Weak, result.Tier);
    }

    // ── Adaptive path: corpus-learned weighted token overlap ──────────────

    /// <summary>A rich JD from an unrelated company, with a distinctive core per company.</summary>
    private static string BatchEntry(int i) =>
        "Our engineering organization is hiring talented developers for team " + i + ". " +
        "You will build scalable " + DomainWord(i) + " systems and contribute to open-source tooling. " +
        "The right person has a background in distributed storage and enjoys mentoring juniors. " +
        "Day-to-day work involves architecture decisions, pair programming, and release automation. " +
        "We look for 5+ years of hands-on coding, deep knowledge of Python or Go, and comfort with Kubernetes clusters. " +
        "Perks include unlimited PTO, remote-first culture, and equity participation for the " + DomainWord(i) + " squad. " +
        "We believe in sustainable pace and psychological safety for every team member. " +
        "Send us your portfolio and let us build something meaningful together.";

    private static string DomainWord(int i) => new[] { "payments", "telemetry", "logistics", "biology", "streaming", "robotics", "tourism", "cryptography", "renewables", "linguistics" }[i % 10];

    private static string[] Tokenize(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (var ch in text)
            sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
        return sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    [Fact]
    public async Task AdaptivePath_ReorderedCopy_FiresWhenShinglesMiss()
    {
        // Same wording, word order reversed inside every sentence: every 5-gram
        // shingle breaks, so the primary path cannot see it; the weighted path can.
        var sentences = LongJd.Split(". ", StringSplitOptions.RemoveEmptyEntries);
        var reordered = string.Join(". ", sentences.Select(s =>
        {
            var words = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return string.Join(' ', words.Reverse().ToArray());
        }).ToArray());
        Assert.NotEqual(reordered, LongJd);

        var corpus = BuildCorpusJson(
            Enumerable.Range(0, 9).Select(i => ("Corp " + i, BatchEntry(i)))
                .Append(("Reordered Corp", reordered))
                .ToArray());

        var posting = BuildPosting("Acme Corp", LongJd, corpus);
        var result = await Signal.EvaluateAsync(posting);

        Assert.NotNull(result);
        Assert.Equal(SignalTier.Weak, result.Tier);
        Assert.InRange(result.Score, 0.40, 0.60);
        Assert.Contains("Reordered Corp", result.Evidence[0]);
        Assert.Contains("different company", result.Evidence[0]);
    }

    [Fact]
    public async Task AdaptivePath_TinyBatch_StatsGated_Off()
    {
        // Without enough batch entries there is no term statistics to learn
        // from; the weighted path refuses to fire even on a reorder.
        var sentences = LongJd.Split(". ", StringSplitOptions.RemoveEmptyEntries);
        var reordered = string.Join(". ", sentences.Select(s =>
        {
            var words = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return string.Join(' ', words.Reverse().ToArray());
        }).ToArray());

        var corpus = BuildCorpusJson(
            ("Corp A", BatchEntry(0)),
            ("Reordered Corp", reordered));

        var posting = BuildPosting("Acme Corp", LongJd, corpus);
        var result = await Signal.EvaluateAsync(posting);

        Assert.Null(result);
    }

    [Fact]
    public async Task AdaptivePath_GenuinelyDifferentJd_FullBatch_StillNull()
    {
        // A realistic-sized batch must not turn generic overlap into a match.
        var corpus = BuildCorpusJson(
            Enumerable.Range(0, 9).Select(i => ("Corp " + i, BatchEntry(i)))
                .Append(("Other Corp",
                    "Our engineering organization is hiring talented developers. " +
                    "You will build scalable systems and contribute to open-source tools. " +
                    "The right person has a background in distributed systems and enjoys mentoring. " +
                    "Day-to-day work involves architecture decisions, pair programming, and release automation. " +
                    "We look for 5+ years of hands-on coding, deep knowledge of Python or Go, and comfort with Kubernetes. " +
                    "Perks include unlimited PTO, remote-first culture, and equity participation. " +
                    "We believe in sustainable pace and psychological safety for every team member. " +
                    "Send us your portfolio and let us build something meaningful together."))
                .ToArray());

        var posting = BuildPosting("Acme Corp", LongJd, corpus);
        var result = await Signal.EvaluateAsync(posting);

        Assert.Null(result);
    }

    [Fact]
    public async Task AdaptivePath_SameCompanyReorder_Excluded()
    {
        var sentences = LongJd.Split(". ", StringSplitOptions.RemoveEmptyEntries);
        var reordered = string.Join(". ", sentences.Reverse().ToArray());

        var corpus = BuildCorpusJson(
            Enumerable.Range(0, 9).Select(i => ("Corp " + i, BatchEntry(i)))
                .Append(("Acme Corp.", reordered)) // DomScorer says same company
                .ToArray());

        var posting = BuildPosting("Acme Corp", LongJd, corpus);
        var result = await Signal.EvaluateAsync(posting);

        Assert.Null(result);
    }

    [Fact]
    public void WeightedMatcher_OnlyRareTokenMass_TriggerMatch()
    {
        // Synthetic, fully controlled: 8 generic boilerplate entries keep the
        // batch gate happy; the target entry shares the posting's rare tokens.
        var batch = new List<(string[] Tokens, string Company)>();
        for (var i = 0; i < 10; i++)
            batch.Add((Tokenize("alpha beta gamma delta epsilon zeta eta theta iota kappa lambda mu nu xi omicron pi rho sigma tau upsilon phi chi psi omega line " + i), "Generic " + i));

        // The posting is plain boilerplate + two rare tokens nobody else has.
        var posting = Tokenize("alpha beta gamma delta epsilon zeta eta theta iota kappa lambda mu nu xi omicron pi rho sigma tau upsilon phi chi psi omega starlight hologram");

        // A matching freelancer-style copy: boilerplate + the same rare tokens, reordered.
        var matchEntry = Tokenize("starlight hologram alpha beta gamma delta epsilon zeta eta theta iota kappa lambda mu nu xi omicron pi rho sigma tau upsilon phi chi psi omega");
        batch.Add((matchEntry, "Distinct Corp"));

        // Another entry shares ONLY the batch-ubiquitous boilerplate: never fires.
        var boilerplateEntry = Tokenize("alpha beta gamma delta epsilon zeta eta theta iota kappa lambda mu nu xi omicron pi rho sigma tau upsilon phi chi psi omega quartz diamond ivory");
        batch.Add((boilerplateEntry, "Boilerplate Only Corp"));

        var matches = DuplicateJdSignal.TryWeightedMatches(posting, entries: batch);

        var fired = matches.Where(m => m.Company == "Distinct Corp").ToList();
        Assert.Single(fired);
        Assert.True(fired[0].WeightedOverlap >= 0.70, $"expected weighted >= 0.70, got {fired[0].WeightedOverlap}");
        Assert.DoesNotContain(matches, m => m.Company == "Boilerplate Only Corp");
        Assert.DoesNotContain(matches, m => m.Company.StartsWith("Generic"));
    }
}
