using Envoy.GhostDetection.Models;
using System.Text.RegularExpressions;

namespace Envoy.GhostDetection.Signals;

/// <summary>
/// Deterministic signal: detects classic job-scam patterns using local regex
/// analysis only (no network). Flags advance-fee fraud, check / overpayment
/// scams, upfront bank/SSN harvesting, off-platform interview redirects, and
/// similar hard tells.
///
/// <para>Precision-first. A single unambiguous tell (e.g. "pay a training fee",
/// "deposit the check we send you", "pay in Bitcoin") can reach the High band;
/// softer indicators (free-email contact, unrealistic pay) only add evidence and
/// can never single-handedly flag a posting. Proximity windows are sentence-bounded
/// (<c>[^.]</c>) so unrelated phrases in different sentences do not combine into a
/// false match. Evidence describes the PATTERN found, never a verdict on the company.</para>
/// </summary>
public class ScamPatternSignal : IGhostSignal
{
    public string Name => "Scam Pattern";
    public SignalTier Tier => SignalTier.Deterministic;
    public bool RequiresNetwork => false;

    // Weights are the per-indicator base Score; an indicator at/above the
    // Deterministic High threshold (0.80) can flag on its own.
    private const double StrongThreshold = 0.70;
    private const double ConvergenceBoost = 0.07;

    public Task<SignalResult?> EvaluateAsync(JobPosting posting, CancellationToken ct = default)
    {
        var text = posting.DescriptionText ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
            return Task.FromResult<SignalResult?>(null);

        var indicators = new List<Indicator>();

        if (CryptoPayment.IsMatch(text) || GiftCardPayment.IsMatch(text))
            indicators.Add(new Indicator(0.90, 0.88,
                "Requests payment in cryptocurrency or gift cards — a hallmark of advance-fee job scams."));

        if (CheckFraud.IsMatch(text))
            indicators.Add(new Indicator(0.88, 0.85,
                "Describes a check-deposit or overpayment arrangement — a classic money-mule / check-fraud pattern."));

        if (UpfrontFee.IsMatch(text) || PayToStart.IsMatch(text) || PurchaseAndReimburse.IsMatch(text))
            indicators.Add(new Indicator(0.85, 0.85,
                "Asks the applicant to pay an upfront fee or buy equipment to start — legitimate employers never charge to apply."));

        if (SensitivePiiUpfront.IsMatch(text))
            indicators.Add(new Indicator(0.82, 0.80,
                "Requests bank, SSN, or other sensitive details at application — employers collect these only after a written offer."));

        if (OffPlatform.IsMatch(text) || TelegramHandle.IsMatch(text))
            indicators.Add(new Indicator(0.70, 0.72,
                "Directs applicants to interview or apply over an off-platform messenger (e.g. Telegram/WhatsApp) — a frequent scam tactic."));

        // ── Weak indicators: add evidence, never flag on their own ──────────
        if (FreeEmailContact.IsMatch(text))
            indicators.Add(new Indicator(0.45, 0.50,
                "Hiring contact uses a free personal email address rather than a company domain."));

        if (UnrealisticPay.IsMatch(text))
            indicators.Add(new Indicator(0.40, 0.45,
                "Advertises pay well above market for a no-experience / entry-level role."));

        if (indicators.Count == 0)
            return Task.FromResult<SignalResult?>(null);

        var strongest = indicators.MaxBy(i => i.Weight)!;
        var strongCount = indicators.Count(i => i.Weight >= StrongThreshold);

        double score = strongest.Weight;
        double confidence = strongest.Confidence;

        // Two or more converging hard tells raise both score and confidence.
        if (strongCount >= 2)
        {
            score = Math.Min(0.95, score + ConvergenceBoost);
            confidence = Math.Min(0.95, confidence + ConvergenceBoost);
        }

        var evidence = indicators
            .OrderByDescending(i => i.Weight)
            .Select(i => i.Evidence)
            .Distinct()
            .Take(4)
            .ToArray();

        return Task.FromResult<SignalResult?>(new SignalResult
        {
            SignalName = Name,
            Score = Math.Round(score, 2),
            Confidence = Math.Round(confidence, 2),
            Evidence = evidence,
            Tier = Tier
        });
    }

    private readonly record struct Indicator(double Weight, double Confidence, string Evidence);

    // ── Pattern bank ────────────────────────────────────────────────────────
    // Proximity windows use [^.] so a match cannot span a sentence boundary,
    // which keeps unrelated phrases (e.g. "we pay well." … "gift cards perk")
    // from combining into a false positive.

    private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant;

    private static Regex Prox(string a, string b, int n = 40) =>
        new($@"(?:{a})[^.]{{0,{n}}}(?:{b})|(?:{b})[^.]{{0,{n}}}(?:{a})", Opts);

    private const string PayVerb = @"\b(?:send|pay(?:ment)?|deposit|transfer|wire|purchase|buy|reimburse)\b";
    private const string CryptoWord = @"\b(?:bitcoin|btc|ethereum|crypto(?:currency)?|usdt|tether|crypto\s*wallet)\b";
    private const string GiftCardWord = @"\b(?:gift|steam|google\s?play|itunes|amazon|vanilla)\s?cards?\b";

    private static readonly Regex CryptoPayment = Prox(CryptoWord, PayVerb, 40);
    private static readonly Regex GiftCardPayment = Prox(GiftCardWord, PayVerb, 40);

    private static readonly Regex CheckFraud = new(
        @"\b(?:cashier'?s|certified|company)\s+checks?\b" +
        @"|\bwe(?:'?ll| will| can)?\s+(?:send|mail|provide|issue)\s+you\s+a\s+check\b" +
        @"|\bdeposit\s+(?:the|this|a|your\s+first)\s+check\b" +
        @"|\bover\s?pay(?:ment)?\b" +
        @"|\bmoney\s+mule\b" +
        @"|\bcash(?:ing)?\s+(?:the|a)\s+check\b", Opts);

    private static readonly Regex UpfrontFee = new(
        @"\b(?:application|registration|training|processing|onboarding|background[\s-]?check|equipment|starter[\s-]?kit|activation)\s+fees?\b", Opts);

    private static readonly Regex PayToStart = new(
        @"\b(?:pay|send|submit|provide|deposit|wire)\b[^.]{0,40}\b(?:fee|deposit|payment)\b[^.]{0,40}\bto\s+(?:start|begin|apply|be\s+considered|secure|qualify)\b", Opts);

    private static readonly Regex PurchaseAndReimburse =
        Prox(@"\b(?:purchase|buy)\b[^.]{0,30}\b(?:equipment|laptop|supplies|materials|software)\b", @"\breimburse\b", 60);

    private const string Sensitive =
        @"\b(?:bank\s+account(?:\s+(?:number|details|info(?:rmation)?))?|routing\s+number|account\s+and\s+routing|social\s+security(?:\s+number)?|ssn|driver'?s?\s+licen[sc]e\s+(?:number|photo|copy)|date\s+of\s+birth)\b";
    private const string UpfrontCtx =
        @"(?:\bto\s+(?:apply|be\s+considered|qualify|proceed)\b|\bbefore\s+(?:the\s+|your\s+|any\s+)?interview\b|\b(?:during|to\s+complete|as\s+part\s+of)\s+(?:the\s+)?application\b|\bto\s+receive\s+(?:your\s+)?(?:first\s+)?(?:payment|salary|paycheck|funds)\b)";

    private static readonly Regex SensitivePiiUpfront = Prox(Sensitive, UpfrontCtx, 60);

    private const string Platform = @"\b(?:telegram|whats\s?app|google\s?hangouts|hangouts|wickr|skype|signal\s+(?:app|messenger))\b";
    private const string OffPlatformAction = @"\b(?:contact|messag(?:e|ing)|text|reach\s+(?:us|out|me)|chat|interview|apply|hiring\s+manager|recruiter|add\s+me|dm|connect)\b";

    private static readonly Regex OffPlatform = Prox(Platform, OffPlatformAction, 50);
    private static readonly Regex TelegramHandle = new(@"\bt\.me/[A-Za-z0-9_]{3,}", Opts);

    private static readonly Regex FreeEmailContact = Prox(
        @"\b[A-Za-z0-9._%+-]+@(?:gmail|yahoo|hotmail|outlook|aol|protonmail|gmx|yandex|mail)\.com\b",
        @"\b(?:resume|cv|application|cover\s+letter)\b", 60);

    private static readonly Regex UnrealisticPay = Prox(
        @"\$\s?(?:[2-9]\d{3,}|\d{1,3},\d{3})\s*(?:/|per\s*)?\s*(?:week|wk)\b|\$\s?(?:[89]\d|\d{3})\s*(?:/|per\s*)?\s*(?:hour|hr)\b",
        @"\b(?:no\s+experience(?:\s+(?:needed|required|necessary))?|entry[\s-]?level|data\s+entry|no\s+skills?\s+(?:needed|required))\b", 120);
}
