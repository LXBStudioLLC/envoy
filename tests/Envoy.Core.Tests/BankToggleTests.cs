using Envoy.Core.Models;
using Envoy.Core.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Envoy.Core.Tests;

/// <summary>
/// APP-10 hardening: stored bank answers can now drive checkbox/radio toggles
/// through the generic fill — normalized ("yes"/"no") so prose never clicks.
/// </summary>
public sealed class BankToggleTests
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
        public Task<string> CompleteAsync(string prompt, string systemPrompt, string? modelId, CancellationToken ct = default) => Task.FromResult(PlanJson);
        public Task<T?> CompleteJsonAsync<T>(string prompt, string systemPrompt, string? modelId, CancellationToken ct = default)
        {
            try { return Task.FromResult(System.Text.Json.JsonSerializer.Deserialize<T>(PlanJson)); }
            catch (System.Text.Json.JsonException) { return Task.FromResult(default(T)); }
        }
    }

    private sealed class FakeEnumerator(List<FormFieldInfo> fields) : IFormFieldEnumerator
    {
        public Task<List<FormFieldInfo>> EnumerateAsync(CancellationToken ct = default) => Task.FromResult(fields);
    }

    private sealed class FakeActuator : IFormFieldActuator
    {
        public Dictionary<string, bool> Checked { get; } = new();
        public List<string> Typed { get; } = new();
        public Task<bool> FillTextAsync(string s, string v, CancellationToken ct = default) { Typed.Add(v); return Task.FromResult(true); }
        public Task<bool> SelectOptionAsync(string s, string o, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> SetCheckedAsync(string s, bool check, CancellationToken ct = default) { Checked[s] = check; return Task.FromResult(true); }
        public Task<bool> UploadFileAsync(string s, string f, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> ExistsAsync(string s, CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed class StaticBank(IReadOnlyList<AnswerBankEntry> entries) : IAnswerBankRepository
    {
        public Task<List<AnswerBankEntry>> GetAllAsync(CancellationToken ct = default) => Task.FromResult(entries.ToList());
        public Task AddOrUpdateAsync(string q, string a, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static FormFieldInfo Checkbox(int i, string label) =>
        new(i, "checkbox", label, "#envoy-field-" + i, false, Array.Empty<string>(), "");

    private static MasterProfile Profile() => new()
    {
        Name = "Mary Ann Smith",
        Email = "mary@example.com",
        Skills = new List<string> { "C#" }
    };

    private static TailoredProfile Tailored() => new()
    {
        MasterProfileId = Guid.NewGuid(),
        JobUrl = "https://x.com/1",
        Company = "c",
        JobTitle = "t",
        JobDescriptionText = "A role. " + new string('d', 150),
        TailoredData = Profile()
    };

    private static AnswerBankEntry Bank(int id, string question, string answer) =>
        new(Guid.NewGuid(), question, answer, DateTime.UtcNow, DateTime.UtcNow);

    [Fact]
    public async Task ToggleWithAffirmativeBankAnswer_ChecksTheBox()
    {
        // The mapper plan: {"fields":[{"index":0,"action":"toggle","value_source":"#0"}]}
        var canned = new CannedProvider
        {
            PlanJson = "{\"fields\":[{\"index\":0,\"action\":\"toggle\",\"value_source\":\"#0\"}]}"
        };

        var bank = new StaticBank(new[]
        {
            Bank(0, "Are you currently authorized to work in the US?", "Yes"),
            Bank(1, "Willing to relocate?", "Yes")
        });

        var fields = new List<FormFieldInfo> { Checkbox(0, "Are you currently authorized to work in the US?") };
        var actuator = new FakeActuator();
        var filler = new GenericFormFiller(
            new FakeEnumerator(fields),
            actuator,
            new FormFieldMapper(new OllamaService(canned), bank),
            Mock.Of<ILogger<GenericFormFiller>>());

        var result = await filler.FillAsync(Tailored(), Profile());

        Assert.Equal(1, result.FilledCount);
        Assert.Empty(result.NeedsHuman);
        Assert.True(actuator.Checked["#envoy-field-0"]);
    }

    [Fact]
    public async Task ToggleWithNegativeBankAnswer_LeavesUnchecked_AndCountsFilled()
    {
        var canned = new CannedProvider
        {
            PlanJson = "{\"fields\":[{\"index\":0,\"action\":\"toggle\",\"value_source\":\"#0\"}]}"
        };

        var bank = new StaticBank(new[]
        {
            Bank(0, "Are you over 55 years old?", "No")
        });

        var fields = new List<FormFieldInfo> { Checkbox(0, "Are you over 55 years old?") };
        var actuator = new FakeActuator();
        var filler = new GenericFormFiller(
            new FakeEnumerator(fields),
            actuator,
            new FormFieldMapper(new OllamaService(canned), bank),
            Mock.Of<ILogger<GenericFormFiller>>());

        var result = await filler.FillAsync(Tailored(), Profile());

        Assert.Equal(1, result.FilledCount);
        Assert.False(actuator.Checked["#envoy-field-0"]);
    }

    [Fact]
    public async Task ToggleWithProseBankAnswer_DropsToNeedsHuman()
    {
        var canned = new CannedProvider
        {
            PlanJson = "{\"fields\":[{\"index\":0,\"action\":\"toggle\",\"value_source\":\"#0\"}]}"
        };

        var bank = new StaticBank(new[]
        {
            Bank(0, "Additional information?", "I am passionate about shipping quality software.")
        });

        var fields = new List<FormFieldInfo> { Checkbox(0, "Additional information?") };
        var actuator = new FakeActuator();
        var filler = new GenericFormFiller(
            new FakeEnumerator(fields),
            actuator,
            new FormFieldMapper(new OllamaService(canned), bank),
            Mock.Of<ILogger<GenericFormFiller>>());

        var result = await filler.FillAsync(Tailored(), Profile());

        Assert.Equal(0, result.FilledCount);
        Assert.Empty(actuator.Checked); // prose never clicks
        Assert.Single(result.NeedsHuman);
        // The prose answer was never slotted, so the model's plan is refused
        // at validation ("toggle must be …#bank") rather than at execution.
        Assert.Contains("toggle must be", result.NeedsHuman.Single(), StringComparison.OrdinalIgnoreCase);
    }
}
