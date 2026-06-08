using Envoy.Core.Models;
using Envoy.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Envoy.Core.Tests;

public class SafetyServiceTests
{
    private readonly SafetyService _sut = new(NullLogger<SafetyService>.Instance);

    private static MasterProfile CreateOriginal() => new()
    {
        Name = "John Doe",
        Email = "john@example.com",
        Skills = new List<string> { "C#", "Python", "SQL" },
        Experience = new List<ExperienceEntry>
        {
            new()
            {
                JobTitle = "Software Engineer",
                Company = "Acme Corp",
                StartDate = "Jan 2020",
                EndDate = "Dec 2022",
                Bullets = new List<string>
                {
                    "Led team of 5 developers",
                    "Increased revenue by 50%",
                    "Built microservices architecture"
                }
            }
        },
        Education = new List<EducationEntry>
        {
            new() { Degree = "BS Computer Science", Institution = "MIT" }
        }
    };

    [Fact]
    public void ValidateTailoredProfile_IdenticalProfile_Passes()
    {
        var original = CreateOriginal();
        var tailored = CreateOriginal();

        var result = _sut.ValidateTailoredProfile(original, tailored, "Looking for a developer");

        Assert.True(result.Passed);
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void ValidateTailoredProfile_InventedSkills_Fails()
    {
        var original = CreateOriginal();
        var tailored = CreateOriginal();
        tailored.Skills = new List<string> { "C#", "Python", "SQL", "Rust", "Go" };

        var result = _sut.ValidateTailoredProfile(original, tailored, "Looking for a developer");

        Assert.False(result.Passed);
        Assert.True(result.ContainsHallucination);
        Assert.Contains(result.Violations, v => v.Field == "Skills");
    }

    [Fact]
    public void ValidateTailoredProfile_ReorderedSkills_Passes()
    {
        var original = CreateOriginal();
        var tailored = CreateOriginal();
        tailored.Skills = new List<string> { "SQL", "Python", "C#" };

        var result = _sut.ValidateTailoredProfile(original, tailored, "Looking for a developer");

        Assert.True(result.Passed);
    }

    [Fact]
    public void ValidateTailoredProfile_NewExperienceEntry_Fails()
    {
        var original = CreateOriginal();
        var tailored = CreateOriginal();
        tailored.Experience = new List<ExperienceEntry>
        {
            new()
            {
                JobTitle = "Senior Developer",
                Company = "Totally Fake Inc",
                StartDate = "Jan 2023",
                EndDate = "Present",
                Bullets = new List<string> { "Did amazing things" }
            }
        };

        var result = _sut.ValidateTailoredProfile(original, tailored, "Looking for a developer");

        Assert.False(result.Passed);
        Assert.True(result.ContainsHallucination);
    }

    [Fact]
    public void ValidateTailoredProfile_NewEducationEntry_Fails()
    {
        var original = CreateOriginal();
        var tailored = CreateOriginal();
        tailored.Education = new List<EducationEntry>
        {
            new() { Degree = "PhD Physics", Institution = "Stanford" }
        };

        var result = _sut.ValidateTailoredProfile(original, tailored, "Looking for a developer");

        Assert.False(result.Passed);
        Assert.True(result.ContainsHallucination);
    }

    [Fact]
    public void ValidateTailoredProfile_DateChange_Fails()
    {
        var original = CreateOriginal();
        var tailored = CreateOriginal();
        tailored.Experience[0].StartDate = "Jan 2021";
        tailored.Experience[0].EndDate = "Dec 2023";

        var result = _sut.ValidateTailoredProfile(original, tailored, "Looking for a developer");

        Assert.True(result.DateInconsistency);
    }

    [Fact]
    public void ValidateTailoredProfile_RephrasedBullet_Passes()
    {
        var original = CreateOriginal();
        var tailored = CreateOriginal();
        tailored.Experience[0].Bullets = new List<string>
        {
            "Led a team of 5 developers",
            "Boosted revenue by 50%",
            "Designed microservices architecture"
        };

        var result = _sut.ValidateTailoredProfile(original, tailored, "Looking for a developer");

        Assert.True(result.Passed);
    }

    [Fact]
    public void ValidateTailoredProfile_FabricatedBullet_Fails()
    {
        var original = CreateOriginal();
        var tailored = CreateOriginal();
        tailored.Experience[0].Bullets = new List<string>
        {
            "Led team of 5 developers",
            "Increased revenue by 50%",
            "Built microservices architecture",
            "Completely invented achievement about quantum computing breakthrough that never happened"
        };

        var result = _sut.ValidateTailoredProfile(original, tailored, "Looking for a developer");

        Assert.False(result.Passed);
        Assert.True(result.ContainsHallucination);
    }

    [Fact]
    public void ValidateTailoredProfile_ExcessiveKeywordDensity_Fails()
    {
        var original = CreateOriginal();
        var tailored = CreateOriginal();
        tailored.Summary = "C# C# C# Python Python Python SQL SQL SQL developer developer developer";

        var jd = "C# Python SQL developer developer developer C# Python SQL developer";
        var result = _sut.ValidateTailoredProfile(original, tailored, jd);

        Assert.True(result.KeywordStuffed);
    }

    [Fact]
    public void ValidateTailoredProfile_ModerateKeywordDensity_Passes()
    {
        var original = CreateOriginal();
        var tailored = CreateOriginal();

        var jd = "We are looking for a talented software engineer with experience in cloud technologies, docker, kubernetes, CI/CD pipelines, and agile methodologies to join our growing team.";
        var result = _sut.ValidateTailoredProfile(original, tailored, jd);

        Assert.True(result.Passed);
        Assert.False(result.KeywordStuffed);
    }
}