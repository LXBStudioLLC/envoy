using System.Text.RegularExpressions;
using Envoy.GhostDetection.Models;

namespace Envoy.GhostDetection.Signals;

/// <summary>
/// Weak signal: a posting whose description is very short, or is almost all
/// boilerplate (mostly EEO, benefits, and legal text) with little
/// role-specific content.
///
/// <para>Recruiters posting real, active roles usually write a substantive
/// description, so a threadbare one is a small sign the listing may be a
/// placeholder or a ghost. Weak and correlational: some real roles do have
/// short descriptions, so the thresholds are deliberately conservative and
/// this signal never moves a posting's band on its own.</para>
///
/// <para>Precision rules: empty or missing text returns null; a short but
/// otherwise real description stays comfortably above the threshold in most
/// legitimate cases because the 350-character bar is low; the
/// boilerplate path only fires when the ratio is very high (75%+) AND almost no
/// role-specific text is left. Never a verdict on a named company.</para>
/// </summary>
public partial class ThinDescriptionSignal : IGhostSignal
{
    public string Name => "Thin Description";
    public SignalTier Tier => SignalTier.Weak;
    public bool RequiresNetwork => false;

    // A posting below this many meaningful characters is threadbare. Low bar:
    // a normal real-world listing runs to thousands of characters.
    private const int ShortDescriptionChars = 350;

    // Boilerplate path: at least this share of the text is boilerplate...
    private const double HighBoilerplateRatio = 0.75;
    // ...and no more than this much role-specific text is left over.
    private const int LittleRoleContentChars = 300;

    private const double Score = 0.30;      // Weak tier: low, never band-moving on its own
    private const double Confidence = 0.55; // low-to-moderate

    // Sentences matching any of these are counted as boilerplate rather than
    // role-specific content. Keep it small and standard; new entries should
    // remain obviously generic legal/HR text.
    private static readonly string[] BoilerplatePhrases =
    {
        "equal opportunity employer", "eo employer", "eoe",
        "reasonable accommodation", "essential functions of the job",
        "benefits include", "comprehensive benefits", "our benefits package",
        "background check", "drug-free workplace", "drug screen",
        "committed to diversity", "diverse and inclusive", "an inclusive workplace",
        "we offer", "at our company", "for more information about",
        "e-verify", "right to work", "at-will", "how to apply",
        "perks and benefits", "meaningful work", "join our team", "apply now"
    };

    public Task<SignalResult?> EvaluateAsync(JobPosting posting, CancellationToken ct = default)
    {
        try
        {
            var cleaned = CollapseWhitespace(posting.DescriptionText);
            if (cleaned.Length < ShortDescriptionChars)
            {
                if (cleaned.Length == 0)
                    return Task.FromResult<SignalResult?>(null); // missing text: nothing to assess

                var score = ComputeScore(cleaned.Length);
                return Task.FromResult<SignalResult?>(new SignalResult
                {
                    SignalName = Name,
                    Score = score,
                    Confidence = Confidence,
                    Evidence = new[]
                    {
                        $"This posting's description is unusually short ({cleaned.Length} characters), which can mean a placeholder listing.",
                        "Some real roles do have short descriptions, so treat this as one small hint."
                    },
                    Tier = Tier
                });
            }

            var (boilerplateChars, ratio) = ComputeBoilerplateShare(posting.DescriptionText, cleaned.Length);
            var roleSpecificChars = cleaned.Length - boilerplateChars;

            if (ratio >= HighBoilerplateRatio && roleSpecificChars <= LittleRoleContentChars)
            {
                return Task.FromResult<SignalResult?>(new SignalResult
                {
                    SignalName = Name,
                    Score = Score,
                    Confidence = Confidence,
                    Evidence = new[]
                    {
                        $"Most of this posting's text ({(int)Math.Round(ratio * 100)}%) is boilerplate — legal, EEO, and benefits language — with only about {roleSpecificChars} characters of role-specific content.",
                        "A description that is almost entirely boilerplate can mean a placeholder listing, though some real postings read this way."
                    },
                    Tier = Tier
                });
            }

            return Task.FromResult<SignalResult?>(null);
        }
        catch
        {
            return Task.FromResult<SignalResult?>(null);
        }
    }

    // ── Text helpers ───────────────────────────────────────────────────
    // Same normalization spirit as DuplicateJdSignal: lowercase, non-letter
    // characters become separators, whitespace collapsed.

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    private static string CollapseWhitespace(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        return Whitespace().Replace(text.Trim(), " ");
    }

    // ── Boilerplate share ──────────────────────────────────────────────

    /// <summary>
    /// Splits the description into sentences and measures how many characters
    /// sit in sentences that match a boilerplate phrase. Returns the
    /// (collapsed) character count of boilerplate text and its share of the
    /// total.
    /// </summary>
    private static (int BoilerplateChars, double Ratio) ComputeBoilerplateShare(string description, int totalChars)
    {
        var sentences = Regex.Split(description.ToLowerInvariant(), @"[.\n;•]+");
        int boilerplateChars = 0;

        foreach (var raw in sentences)
        {
            var sentence = Whitespace().Replace(raw.Trim(), " ");
            if (sentence.Length == 0)
                continue;

            // A short generic fragment is content, not boilerplate, on its own —
            // it must match a standard boilerplate phrase to count.
            if (BoilerplatePhrases.Any(phrase => sentence.Contains(phrase, StringComparison.Ordinal)))
                boilerplateChars += sentence.Length;
        }

        var ratio = totalChars == 0 ? 0 : (double)boilerplateChars / totalChars;
        return (boilerplateChars, Math.Round(ratio, 2));
    }

    // ── Scoring ────────────────────────────────────────────────────────

    private static double ComputeScore(int length)
    {
        // The emptier the description, the stronger (still capped low) the hint.
        if (length < 120)
            return 0.45;
        if (length < 250)
            return 0.38;
        return 0.30;
    }
}
