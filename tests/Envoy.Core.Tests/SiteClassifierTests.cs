using Envoy.Core.Services;
using Xunit;

namespace Envoy.Core.Tests;

/// <summary>
/// APP-11: the in-code automation boundary. Aggregators are copilot-only
/// (auto-submit never targets them), unknown domains are cautious, and known
/// ATS boards pass the gate — with tenant subdomains inheriting the class.
/// </summary>
public sealed class SiteClassifierTests
{
    [Theory]
    [InlineData("https://www.linkedin.com/jobs/view/123", SiteClassifier.SiteChannel.Aggregator)]
    [InlineData("https://indeed.com/viewjob?jk=1", SiteClassifier.SiteChannel.Aggregator)]
    [InlineData("https://www.ziprecruiter.com/jobs/1", SiteClassifier.SiteChannel.Aggregator)]
    [InlineData("https://www.wellfound.com/jobs", SiteClassifier.SiteChannel.Aggregator)]
    [InlineData("https://boards.greenhouse.io/acme/jobs/1", SiteClassifier.SiteChannel.CompanyAts)]
    [InlineData("https://job-boards.greenhouse.io/acme/jobs/1", SiteClassifier.SiteChannel.CompanyAts)]
    [InlineData("https://acme.wd5.myworkdaysite.com/recruiting/acme/Job/1", SiteClassifier.SiteChannel.CompanyAts)]
    [InlineData("https://myworkdayjobs.com/my", SiteClassifier.SiteChannel.CompanyAts)]
    [InlineData("https://jobs.smartrecruiters.com/Acme/123", SiteClassifier.SiteChannel.CompanyAts)]
    [InlineData("https://jobs.lever.co/acme/1", SiteClassifier.SiteChannel.CompanyAts)]
    [InlineData("https://jobs.ashbyhq.com/acme/1", SiteClassifier.SiteChannel.CompanyAts)]
    [InlineData("https://apply.workable.com/acme/j/1", SiteClassifier.SiteChannel.CompanyAts)]
    [InlineData("https://acme.icims.com/jobs/0001/job", SiteClassifier.SiteChannel.CompanyAts)]
    [InlineData("https://acme.taleo.net/careersection/1", SiteClassifier.SiteChannel.CompanyAts)]
    public void Channels_ClassifyCorrectly(string url, SiteClassifier.SiteChannel expected)
    {
        Assert.Equal(expected, SiteClassifier.Classify(url));
    }

    [Fact]
    public void UnknownDomains_AreNotAutoSubmittable()
    {
        Assert.False(SiteClassifier.IsAutoSubmittable("https://careers.somethingrandom.dev/job/1"));
        Assert.False(SiteClassifier.IsAutoSubmittable(null));
        Assert.False(SiteClassifier.IsAutoSubmittable("not-a-url"));
        // Even a plausible impersonation stays unknown unless it matches.
        Assert.False(SiteClassifier.IsAutoSubmittable("https://greenhouse.io.evil.example/jobs/1"));
    }

    [Fact]
    public void SubdomainPrefix_DoesNotPoisonTheHost()
    {
        // example.linkedin.com.evil.example → host's last label is example, no
        // suffix match, so it's NOT an aggregator → stays Unknown → gate denies.
        Assert.Equal(SiteClassifier.SiteChannel.Unknown, SiteClassifier.Classify("https://linkedin.com.evil.example/jobs/1"));
    }

    [Fact]
    public void Gate_DeniesAggregatorsAndUnknowns_AffirmsCompanyAts()
    {
        Assert.True(SiteClassifier.IsAutoSubmittable("https://boards.greenhouse.io/a/1"));
        Assert.False(SiteClassifier.IsAutoSubmittable("https://www.linkedin.com/jobs/1"));
        Assert.False(SiteClassifier.IsAutoSubmittable("https://careers.totallyunknown.site/jobs/1"));
    }
}
