using Envoy.Core.Models;
using Envoy.Core.Services;
using Xunit;

namespace Envoy.Core.Tests;

/// <summary>
/// APP-09: the field mapper's validation is the invention firewall. Plans are
/// strict offline artifacts (no model, no network); UIWhoever produces them
/// must obey the structural whitelist.
/// </summary>
public sealed class FormFieldMapperValidationTests
{
    private static FormFieldInfo Text(int i, string label = "Email Address", string type = "email") =>
        new(i, type, label, "#envoy-field-" + i, false, Array.Empty<string>(), "");
    private static FormFieldInfo Select(int i, string label, params string[] options) =>
        new(i, "select", label, "#envoy-field-" + i, false, options, "");
    private static FormFieldMapper.RawPlanItem Plan(int i, string? action, string? value) =>
        new(i, action, value, null);

    /// <summary>An offered-slot list for validation; count-based like the old bankCount. Text slots by default; toggle tests assert the pf: grammar.</summary>
    private static IReadOnlyList<FormFieldMapper.SlotCandidate> Offered(int count) =>
        Enumerable.Range(0, count).Select(n => new FormFieldMapper.SlotCandidate(
            n,
            n, // BankId is positional here (id-space slot)
            new AnswerBankEntry(Guid.NewGuid(), $"Stored question {n}", "Yes", DateTime.UtcNow, DateTime.UtcNow),
            $"Live label {n}",
            $"Stored answer {n}", // text for fill plans
            null)).ToList(); // toggles are exercised with pf: grammar in this suite

    [Fact]
    public void GoodFillPlan_WithKnownKey_Passes()
    {
        var fields = new[] { Text(0), Text(1, "First Name", "text") };
        var raw = new[] { Plan(0, "fill", "pf:email"), Plan(1, "fill", "pf:first_name") };

        var plans = FormFieldMapper.Validate(fields, raw, Offered(0));

        Assert.All(plans, p => Assert.NotEqual(FieldPlanAction.NeedsHuman, p.Action));
        Assert.Equal(2, plans.Count(p => p.Action == FieldPlanAction.Fill));
    }

    [Fact]
    public void InventedValue_IsRefused()
    {
        var fields = new[] { Text(0) };
        var raw = new[] { Plan(0, "fill", "I am a great fit!") }; // free text = invention

        var plans = FormFieldMapper.Validate(fields, raw, Offered(0));

        var p = Assert.Single(plans);
        Assert.Equal(FieldPlanAction.NeedsHuman, p.Action);
        Assert.Equal("invented value refused", p.Note);
    }

    [Fact]
    public void UnknownProfileKey_IsRefused()
    {
        var fields = new[] { Text(0) };
        var raw = new[] { Plan(0, "fill", "pf:hacked_key_that_doesnt_exist") };

        var plans = FormFieldMapper.Validate(fields, raw, Offered(0));

        Assert.Equal(FieldPlanAction.NeedsHuman, Assert.Single(plans).Action);
    }

    [Fact]
    public void Select_RequiresExactOption_OrElseNeedsHuman()
    {
        var fields = new[] { Select(0, "Years of experience", "0-1", "1-3", "4+") };
        var raw = new[] { Plan(0, "select", "2 years") }; // no exact option

        var plans = FormFieldMapper.Validate(fields, raw, Offered(0));

        var p = Assert.Single(plans);
        Assert.Equal(FieldPlanAction.NeedsHuman, p.Action);
        Assert.Equal("no exactly matching option", p.Note);
    }

    [Fact]
    public void Select_ExactOption_Passes()
    {
        var fields = new[] { Select(0, "Years of experience", "0-1", "1-3", "4+") };
        var raw = new[] { Plan(0, "select", "4+") };

        var plans = FormFieldMapper.Validate(fields, raw, Offered(0));

        Assert.Equal(FieldPlanAction.Select, Assert.Single(plans).Action);
    }

    [Fact]
    public void Toggle_MustBeBool()
    {
        var fields = new[] { new FormFieldInfo(0, "checkbox", "Over 18", "#c18", false, Array.Empty<string>(), "") };
        var raw = new[] { Plan(0, "toggle", "pf:yes_please") };

        var plans = FormFieldMapper.Validate(fields, raw, Offered(0));

        Assert.Equal(FieldPlanAction.NeedsHuman, Assert.Single(plans).Action);
    }

    [Fact]
    public void Toggle_Bool_Passes()
    {
        var fields = new[] { new FormFieldInfo(0, "checkbox", "Over 18", "#c18", false, Array.Empty<string>(), "") };
        var raw = new[] { Plan(0, "toggle", "pf:true") };

        var plans = FormFieldMapper.Validate(fields, raw, Offered(0));

        Assert.Equal(FieldPlanAction.Toggle, Assert.Single(plans).Action);
    }

    [Fact]
    public void BankCandidate_WithinRange_Passes()
    {
        var fields = new[] { Text(0, "Tell us about yourself", "textarea") };
        var raw = new[] { Plan(0, "fill", "#0") }; // the one candidate the prompt offered

        var plans = FormFieldMapper.Validate(fields, raw, Offered(1));

        Assert.Equal(FieldPlanAction.Fill, Assert.Single(plans).Action);
    }

    [Fact]
    public void BankCandidate_OutOfRange_IsRefused()
    {
        var fields = new[] { Text(0, "Tell us about yourself", "textarea") };
        var raw = new[] { Plan(0, "fill", "#7") };

        var plans = FormFieldMapper.Validate(fields, raw, Offered(1));

        Assert.Equal(FieldPlanAction.NeedsHuman, Assert.Single(plans).Action);
    }

    [Fact]
    public void UnmappedFields_DefaultToNeedsHuman()
    {
        var fields = new[] { Text(0), Text(1, "Zip Code", "text") };
        var raw = new[] { Plan(0, "fill", "pf:email") };

        var plans = FormFieldMapper.Validate(fields, raw, Offered(0));

        var unmapped = plans.Single(p => p.Index == 1);
        Assert.Equal(FieldPlanAction.NeedsHuman, unmapped.Action);
        Assert.Equal("unmapped", unmapped.Note);
    }

    [Fact]
    public void DuplicatePlans_FirstWins()
    {
        var fields = new[] { Text(0) };
        var raw = new[]
        {
            Plan(0, "fill", "pf:email"),
            Plan(0, "fill", "pf:first_name") // second plan for the same field: ignored
        };

        var plans = FormFieldMapper.Validate(fields, raw, Offered(0));

        var p = Assert.Single(plans.Where(x => x.Index == 0));
        Assert.Equal("pf:email", p.ValueSource);
    }

    /// <summary>The no-evidence firewall from the prompt, verifiable in tests: no EEO/salary keys may leak in the catalog.</summary>
    [Fact]
    public void EeoAndSalary_CannotComeFromProfileCatalog()
    {
        var catalogKeys = FormFieldPrompts.Catalog.Select(c => c.Key).ToList();

        Assert.DoesNotContain(catalogKeys, k => k.Contains("salary"));
        Assert.DoesNotContain(catalogKeys, k => k.Contains("gender"));
        Assert.DoesNotContain(catalogKeys, k => k.Contains("race"));
        Assert.DoesNotContain(catalogKeys, k => k.Contains("veteran"));
        Assert.DoesNotContain(catalogKeys, k => k.Contains("sponsorship"));
    }
}
