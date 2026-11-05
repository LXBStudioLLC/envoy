using Envoy.Core.Data;
using Envoy.Core.Models;
using Envoy.Core.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Envoy.Core.Tests;

/// <summary>
/// APP-10: TemplateEngine.GetValueFromProfile covers the full documented
/// field-id list (TEMPLATE_AUTHORING promises it; before this test the other
/// ten ids silently filled "" in Release builds).
/// </summary>
public sealed class DocumentedFieldIdTests
{
    // The shared profile used by all id assertions.
    private static TailoredProfile Profile() => new()
    {
        Company = "ExampleCo",
        JobTitle = "Senior Backend Engineer",
        CoverLetterText = "Dear ExampleCo team — the Cloud-scale ledger work speaks for itself.",
        TailoredData = new MasterProfile
        {
            Name = "Mary Ann Smith",
            Email = "mary@example.com",
            Phone = "+1 555 0100",
            Location = "Seattle, WA",
            LinkedIn = "linkedin.com/in/maryannsmith",
            Website = "maryann.dev",
            Summary = "I build resilient systems where honesty beats heroics.",
            Skills = new List<string> { "C#", "Azure" },
            Experience =
            {
                new ExperienceEntry
                {
                    JobTitle = "Principal Engineer",
                    Company = "Whaletail",
                    StartDate = "2019-03",
                    IsCurrent = true,
                    Bullets = new List<string> { "Scaled search 40x" }
                }
            }
        }
    };

    private static List<string> TypedValueFor(string valueFrom)
    {
        var browser = new TemplateEngineGateTests.FakePageInteractor();
        var engine = new TemplateEngine(
            Path.Combine(Path.GetTempPath(), "envoy-no-templates-" + Guid.NewGuid()), null);

        var step = new TemplateStep { Action = "fill", Selector = "#x", ValueFrom = valueFrom };
        var template = new SiteTemplate { Id = "t", Name = "T", UrlMatch = "example.com/*" };
        template.Steps.Add(step);

        _ = engine.ExecuteTemplateAsync(template, browser, Profile(), _ => Task.FromResult(true)).GetAwaiter().GetResult();
        return browser.Typed;
    }

    [Fact]
    public void DocumentedIds_AllResolve()
    {
        var ids = new[]
        {
            ("pf-first_name", "first_name", "Mary"),
            ("pf-last_name", "last_name", "Ann Smith"),
            ("pf-phone", "phone", "+1 555 0100"),
            ("pf-linkedin_url", "linkedin_url", "linkedin.com/in/maryannsmith"),
            ("pf-portfolio_url", "portfolio_url", "maryann.dev"),
            ("pf-current_company", "current_company", "Whaletail"),
            ("pf-current_title", "current_title", "Principal Engineer"),
            ("pf-city", "city", "Seattle"),
            ("pf-state", "state", "WA"),
            ("pf-years_experience", "years_experience", (string?)null),
            ("pf-cover_letter", "cover_letter", (string?)null)
        };

        foreach (var (id, valueFrom, exact) in ids)
        {
            var values = TypedValueFor(valueFrom);
            var v = Assert.Single(values);
            if (exact is not null)
                Assert.True(v == exact, $"{id}: expected '{exact}', got '{v}'");
            else
                Assert.True(v.Length > 0, $"{id}: expected a non-empty value");
        }

        var email = TypedValueFor("email");
        Assert.Contains("mary@example.com", Assert.Single(email));
        Assert.Contains("Whaletail", Assert.Single(TypedValueFor("org")));
        Assert.Contains("Whaletail", Assert.Single(TypedValueFor("company")));
        Assert.Contains("Principal Engineer", Assert.Single(TypedValueFor("current_title")));

        // zip has no model data: an unresolved fill never produces an empty
        // string on a live form — it's skipped (and logged as missing).
        Assert.Empty(TypedValueFor("zip"));
    }

    [Fact]
    public async void CoverLetter_PersistsViaEfColumn()
    {
        var connectionString = "Data Source=envoy-cl-" + Guid.NewGuid().ToString("N") + ".db";
        try
        {
            var options = new DbContextOptionsBuilder<EnvoyDbContext>().UseSqlite(connectionString).Options;
            using (var db = new EnvoyDbContext(options))
            {
                db.Database.EnsureCreated();
                db.TailoredProfiles.Add(new TailoredProfile
                {
                    MasterProfileId = Guid.NewGuid(),
                    JobUrl = "https://x.com/1",
                    Company = "c",
                    JobTitle = "t",
                    CoverLetterText = "letter body"
                });
                db.SaveChanges();
            }

            using (var check = new EnvoyDbContext(options))
            {
                var row = check.TailoredProfiles.First();
                Assert.Equal("letter body", row.CoverLetterText);
            }
        }
        finally
        {
            try { File.Delete(connectionString.Replace("Data Source=", "")); } catch { }
        }
    }
}
