using Envoy.Core.Services;
using Xunit;

namespace Envoy.Core.Tests;

/// <summary>
/// APP-01 guard: the bundled site templates must ride along in EVERY build,
/// not only the installer path. `Envoy.Templates.csproj` copies its JSON to
/// output, and this project references it — so `AppContext.BaseDirectory`
/// must contain a `Templates` folder with loadable templates. If these tests
/// fail, a plain `dotnet run --project src/Envoy.UI` fails every application
/// with "No template found for this job board".
/// </summary>
public sealed class TemplateShippingTests
{
    [Fact]
    public void Templates_DirectoryShipsWithBuilds()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Templates");
        Assert.True(Directory.Exists(path), $"templates directory missing at {path}");

        var jsonFiles = Directory.EnumerateFiles(path, "*.json").ToList();
        Assert.True(jsonFiles.Count >= 5, $"expected the 5 bundled templates, found {jsonFiles.Count}");
    }

    [Fact]
    public void ShippedTemplates_LoadAndMatchTheirBoards()
    {
        var engine = new TemplateEngine(Path.Combine(AppContext.BaseDirectory, "Templates"));

        // Each bundled template file must parse and match a sample posting on its own domain.
        var cases = new (string TemplateFile, string SampleUrl)[]
        {
            ("greenhouse-default.json", "https://boards.greenhouse.io/example/jobs/123"),
            ("lever-default.json", "https://jobs.lever.co/example/123"),
            ("workday-default.json", "https://wd5.myworkdayjobs.com/example/job/123"),
            ("linkedin-easy-apply.json", "https://www.linkedin.com/jobs/view/123"),
            ("indeed-apply.json", "https://www.indeed.com/viewjob?jk=123")
        };

        foreach (var (file, url) in cases)
        {
            var template = engine.MatchTemplate(url);
            Assert.True(template is not null, $"{file} did not load or does not match its sample URL ({url})");
            Assert.True(template!.Steps.Count > 0, $"{file} loaded with zero steps");
        }
    }
}
