using Envoy.Core.Models;
using Envoy.Core.Services;
using System.Text.Json;
using Xunit;

namespace Envoy.Core.Tests;

/// <summary>
/// APP-09: the generic fill pipeline end-to-end with an in-memory
/// enumerator/actuator pair and a canned mapper (offline, zero LLM raw-work —
/// the canned provider returns a valid plan script WHICH IS STILL VALIDATED:
/// every structural firewall applies to it identically).
/// </summary>
public sealed class GenericFormFillerTests : IDisposable
{
    private sealed class CannedProvider : ILLMProvider
    {
        public string PlanJson { get; set; } = "{}";
        public string ProviderId => "canned";
        public string DisplayName => "Canned";
        public string Description => "canned plan";
        public bool RequiresApiKey => false;
        public bool IsLocal => true;

        public Task<bool> IsAvailableAsync() => Task.FromResult(true);
        public Task<List<LLMModelInfo>> ListModelsAsync() => Task.FromResult(new List<LLMModelInfo>());
        public Task<string> CompleteAsync(string prompt, string systemPrompt, string? modelId, CancellationToken ct = default)
            => Task.FromResult(PlanJson);

        public Task<T?> CompleteJsonAsync<T>(string prompt, string systemPrompt, string? modelId, CancellationToken ct = default)
        {
            try { return Task.FromResult(JsonSerializer.Deserialize<T>(PlanJson)); }
            catch (JsonException) { return Task.FromResult(default(T)); }
        }

        private static readonly System.Text.Json.JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

        private static T? Deserialize<T>(string json) where T : class
        {
            try { return System.Text.Json.JsonSerializer.Deserialize<T>(json, JsonOpts); }
            catch (System.Text.Json.JsonException) { return null; }
        }
    }

    private sealed class FakeEnumerator(List<FormFieldInfo> fields) : IFormFieldEnumerator
    {
        public List<FormFieldInfo> Fields => fields;
        public Task<List<FormFieldInfo>> EnumerateAsync(CancellationToken ct = default) => Task.FromResult(fields);
    }

    private sealed class FakeActuator : IFormFieldActuator
    {
        public List<string> Typed { get; } = new();
        public Dictionary<string, string> Selected { get; } = new();
        public Dictionary<string, bool> Checked { get; } = new();
        public List<string> Uploaded { get; } = new();
        public bool Executed { get; set; } = true;

        public Task<bool> FillTextAsync(string s, string v, CancellationToken ct = default) { if (!Executed) return Task.FromResult(false); Typed.Add(s + "=" + v); return Task.FromResult(true); }
        public Task<bool> SelectOptionAsync(string s, string option, CancellationToken ct = default) { if (!Executed) return Task.FromResult(false); Selected[s] = option; return Task.FromResult(true); }
        public Task<bool> SetCheckedAsync(string s, bool check, CancellationToken ct = default) { Checked[s] = check; return Task.FromResult(true); }
        public Task<bool> UploadFileAsync(string s, string file, CancellationToken ct = default) { Uploaded.Add(s); return Task.FromResult(true); }
        public Task<bool> ExistsAsync(string s, CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed class StaticBank(IReadOnlyList<AnswerBankEntry> entries) : IAnswerBankRepository
    {
        public Task<List<AnswerBankEntry>> GetAllAsync(CancellationToken ct = default) => Task.FromResult(entries.ToList());
        public Task AddOrUpdateAsync(string q, string a, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StaticProfileRepo : IProfileRepository
    {
        private readonly MasterProfile _profile;
        public StaticProfileRepo(MasterProfile profile) => _profile = profile;
        public Task<MasterProfile?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<MasterProfile?>(_profile);
        public Task AddAsync(MasterProfile profile, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(MasterProfile profile, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<MasterProfile>> GetAllAsync(CancellationToken ct = default) => Task.FromResult(new List<MasterProfile> { _profile });
    }

    public void Dispose()
    {
        try { Directory.Delete(Path.Combine(Path.GetTempPath(), "envoy-generic-filler"), true); } catch { }
    }

    private static FormFieldInfo Field(int i, string type, string label, params string[] options) =>
        new(i, type, label, "#envoy-field-" + i, false, options, "");

    private static MasterProfile ProfileRecord() => new()
    {
        Name = "Mary Ann Smith",
        Email = "mary@example.com",
        Phone = "+1 555 0100",
        Location = "Seattle, WA",
        LinkedIn = "linkedin.com/in/maryannsmith",
        Summary = "Cloud-scale systems veteran, resilient under ambiguity and honest about constraints.",
        Skills = new List<string> { "C#", "Distributed systems", "Azure" },
        Experience =
        {
            new ExperienceEntry
            {
                JobTitle = "Principal Engineer",
                Company = "Whaletail",
                StartDate = "2019-03",
                EndDate = null,
                IsCurrent = true,
                Bullets = new List<string> { "Shipped the payments platform rewrite", "Scaled search 40x" }
            }
        }
    };

    private static TailoredProfile Tailored(string resumPath = "") => new()
    {
        MasterProfileId = Guid.NewGuid(),
        JobUrl = "https://boards.example.io/jobs/1",
        Company = "ExampleCo, Inc.",
        JobTitle = "Software Engineer",
        JobDescriptionText = "Build and ship cloud systems in C# with a small friendly team.",
        TailoredData = ProfileRecord(),
        MatchScore = 72
    };

    [Fact]
    public async Task HappyPath_FillsSelectsChecks_andUploads()
    {
        var json = """
        {"fields":[
          {"index":0,"action":"fill","value_source":"pf:email"},
          {"index":1,"action":"fill","value_source":"pf:first_name"},
          {"index":2,"action":"select","value_source":"4+"},
          {"index":3,"action":"toggle","value_source":"pf:true"},
          {"index":4,"action":"upload","value_source":"pf:resume_file"},
          {"index":5,"action":"skip","value_source":null},
          {"index":6,"action":"needs_human","value_source":null,"note":"解释"}
        ]}
        """;
        var canned = new CannedProvider { PlanJson = json };

        var fields = new List<FormFieldInfo>
        {
            Field(0, "email", "Email Address"),
            Field(1, "text", "First Name"),
            Field(2, "select", "Years of experience", "0-1", "4+"),
            Field(3, "checkbox", "US resident"),
            Field(4, "file", "Resume"),
            Field(5, "text", "Referred by"),
            Field(6, "textarea", "Tell us about your worst debugging experience")
        };

        var actuator = new FakeActuator();
        var filler = new GenericFormFiller(
            new FakeEnumerator(fields),
            actuator,
            new FormFieldMapper(new OllamaService(canned), new StaticBank(Array.Empty<AnswerBankEntry>())));

        var tailored = Tailored();
        var pdf = ResumeFilePath.For(tailored.TailoredData.Name, tailored.Company, tailored.JobTitle);
        Directory.CreateDirectory(Path.GetDirectoryName(pdf)!);
        await File.WriteAllTextAsync(pdf, "fake pdf bytes for testing");

        var result = await filler.FillAsync(tailored, tailored.TailoredData);

        // 5 of 7: email, first_name, select, checkbox, upload; skip adds nothing; needs_human adds none.
        var humanNote = string.Join("; ", result.NeedsHuman);
        Assert.True(result.FilledCount == 5,
            $"filled={result.FilledCount}, needsHuman=[{humanNote}], notes=[{string.Join("; ", result.Notes)}]");
        Assert.Equal(7, result.FieldCount);
        Assert.Single(result.NeedsHuman);
        Assert.Contains("worst debugging", result.NeedsHuman.Single());
        Assert.Contains(actuator.Typed, t => t.Contains("mary@example.com"));
        Assert.True(actuator.Selected.ContainsKey("#envoy-field-2"));
        Assert.True(actuator.Checked.ContainsKey("#envoy-field-3"));
        Assert.Single(actuator.Uploaded);
    }

    [Fact]
    public async Task NoFields_HonestNoOp()
    {
        var filler = new GenericFormFiller(
            new FakeEnumerator(new List<FormFieldInfo>()),
            new FakeActuator(),
            new FormFieldMapper(new OllamaService(new CannedProvider()), new StaticBank(Array.Empty<AnswerBankEntry>())));

        var result = await filler.FillAsync(Tailored(), ProfileRecord());

        Assert.Equal(0, result.FieldCount);
        Assert.Contains(result.Notes, n => n.Contains("No fillable form fields"));
    }

    [Fact]
    public async Task UnmappedModel_DegradestoNeedsHuman()
    {
        var fields = new List<FormFieldInfo> { Field(0, "email", "Email Address") };

        var filler = new GenericFormFiller(
            new FakeEnumerator(fields),
            new FakeActuator(),
            new FormFieldMapper(new OllamaService(new CannedProvider()), new StaticBank(Array.Empty<AnswerBankEntry>())));

        var result = await filler.FillAsync(Tailored(), ProfileRecord());

        Assert.Equal(0, result.FilledCount);
        Assert.Single(result.NeedsHuman);
    }

    [Fact]
    public void CatalogYearsExperience_MergesRanges()
    {
        var profile = ProfileRecord();
        profile.Experience.Add(new ExperienceEntry
        {
            JobTitle = "Senior",
            Company = "OldCorp",
            StartDate = "2021-01",
            EndDate = "2021-02",
            IsCurrent = false,
            Bullets = new List<string>()
        });

        // 2019-03 → now is one 6.x-year span; the second job overlaps it and
        // must NOT double-count months.
        var years = FormProfileCatalog.Resolve("years_experience", profile);
        var value = double.Parse(years!, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(value > 5.5 && value < 20, $"years computed = {value}");
    }

    [Fact]
    public void Catalog_ContactBasics_Resolve()
    {
        var profile = ProfileRecord();

        Assert.Equal("mary@example.com", FormProfileCatalog.Resolve("email", profile));
        Assert.Equal("Mary", FormProfileCatalog.Resolve("first_name", profile));
        Assert.Equal("Ann Smith", FormProfileCatalog.Resolve("last_name", profile));
        Assert.Equal("+1 555 0100", FormProfileCatalog.Resolve("phone", profile));
        Assert.Contains("Principal Engineer", FormProfileCatalog.Resolve("current_title", profile));
        Assert.Contains("Whaletail", FormProfileCatalog.Resolve("current_company", profile));
        Assert.Contains("C#", FormProfileCatalog.Resolve("skills", profile));
    }
}
