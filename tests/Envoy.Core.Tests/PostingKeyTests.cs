using Envoy.Core.Services;
using Xunit;

namespace Envoy.Core.Tests;

public class PostingKeyTests
{
    [Fact]
    public void SamePosting_ReachedViaTrackingNoise_ProducesOneKey()
    {
        var clean = PostingKey.For("https://boards.greenhouse.io/acme/jobs/123", "Acme", "Engineer");
        var noisy = PostingKey.For(
            "https://www.Boards.Greenhouse.io/Acme/jobs/123/?utm_source=linkedin&ref=share&gclid=abc",
            "Acme", "Engineer");

        Assert.Equal(clean, noisy);
        Assert.Equal("boards.greenhouse.io/acme/jobs/123", clean);
    }

    [Fact]
    public void QueryParameterOrder_DoesNotChangeTheKey()
    {
        var a = PostingKey.For("https://jobs.example.com/apply?a=1&b=2", null, null);
        var b = PostingKey.For("https://jobs.example.com/apply?b=2&a=1", null, null);

        Assert.Equal(a, b);
    }

    [Fact]
    public void MeaningfulQueryParams_KeepDistinctJobsDistinct()
    {
        // Company career pages often carry the job id in the query (e.g. gh_jid).
        var first = PostingKey.For("https://acme.com/careers?gh_jid=111", "Acme", "Engineer");
        var second = PostingKey.For("https://acme.com/careers?gh_jid=222", "Acme", "Engineer");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void NoUsableUrl_FallsBackToNormalizedCompanyAndTitle()
    {
        var fromEmpty = PostingKey.For("", "  Acme  Corp ", "Senior  Engineer");
        var fromMalformed = PostingKey.For("not a url", "acme corp", "senior engineer");

        Assert.Equal("acme corp|senior engineer", fromEmpty);
        Assert.Equal(fromEmpty, fromMalformed);
    }
}
