using JobHuntHQ.Models;

namespace JobHuntHQ.Services;

/// <summary>The outcome of one fit check: a verdict, the human-readable reasons behind it, and the raw score.</summary>
public sealed record FitCheckResult(string Verdict, IReadOnlyList<string> Reasons, int Score, bool Blocked)
{
    /// <summary>The reasons as one stored string, one reason per line.</summary>
    public string ReasonsText => string.Join("\n", Reasons);
}

/// <summary>
/// Deterministic fit check (M4): scores a captured posting against the user's
/// own <see cref="SearchProfile"/> with fixed rules and plain-language
/// reasons. No external calls, no AI — the same inputs always give the same
/// verdict, and every point is explained.
///
/// Rules (each appends a reason line):
///   (a) Company on the skip-companies list                    → Blocked.
///   (b) Any deal-breaker keyword found in the posting text    → Blocked.
///   (c) Pay: posting max below the pay floor                  → −2;
///       posting min at/above the pay floor                    → +2;
///       a range that merely overlaps the floor                → ±0 (noted);
///       floor set but no pay captured                         → ±0 (noted).
///   (d) Each must-have keyword: present in the posting text   → +1;
///       missing                                               → −1.
///   (e) Any target-role keyword in the title or posting text  → +2;
///       target roles set but none matched                     → −1.
/// Keyword checks (b)/(d) need posting text; without it they are skipped and
/// a "no posting text" reason is recorded instead. A profile with no criteria
/// at all yields "Not checked", as does having no profile.
/// Verdict from score (when not Blocked / Not checked):
///   ≥ 3 "Strong fit", 1–2 "Possible", ≤ 0 "Poor fit".
/// </summary>
public class FitCheckService
{
    public const string VerdictBlocked = "Blocked";
    public const string VerdictPoorFit = "Poor fit";
    public const string VerdictPossible = "Possible";
    public const string VerdictStrongFit = "Strong fit";
    public const string VerdictNotChecked = "Not checked";

    public FitCheckResult Evaluate(JobApplication application, SearchProfile? profile)
    {
        if (profile is null)
        {
            return new FitCheckResult(
                VerdictNotChecked,
                new[] { "Create a search profile to enable fit checks." },
                0,
                false);
        }

        var reasons = new List<string>();
        var score = 0;
        var blocked = false;

        var postingText = application.PostingText ?? string.Empty;
        var hasText = !string.IsNullOrWhiteSpace(postingText);

        // (a) Skip companies — exact (case-insensitive) or the company name
        // containing the listed entry, so "Acme" also blocks "Acme Corp".
        foreach (var skip in ParseList(profile.SkipCompanies))
        {
            if (application.Company.Contains(skip, StringComparison.OrdinalIgnoreCase))
            {
                blocked = true;
                reasons.Add($"Blocked: {application.Company} is on your skip-companies list.");
                break;
            }
        }

        if (!hasText)
        {
            reasons.Add("No posting text captured yet — paste the posting to check deal-breakers and must-haves.");
        }
        else
        {
            // (b) Deal-breakers found in the posting text → Blocked.
            foreach (var keyword in ParseList(profile.DealBreakers))
            {
                if (ContainsKeyword(postingText, keyword))
                {
                    blocked = true;
                    reasons.Add($"Blocked: the posting mentions your deal-breaker “{keyword}”.");
                }
            }

            // (d) Must-haves present / missing in the posting text.
            foreach (var keyword in ParseList(profile.MustHaves))
            {
                if (ContainsKeyword(postingText, keyword))
                {
                    score += 1;
                    reasons.Add($"Must-have found: “{keyword}”.");
                }
                else
                {
                    score -= 1;
                    reasons.Add($"Must-have missing: “{keyword}” is not mentioned in the posting.");
                }
            }
        }

        // (c) Pay versus the user's floor.
        if (profile.PayFloor is int floor)
        {
            if (application.PayMax is int payMax && payMax < floor)
            {
                score -= 2;
                reasons.Add($"Pay: the posting tops out at {Money(payMax)}, below your floor of {Money(floor)}.");
            }
            else if (application.PayMin is int payMin && payMin >= floor)
            {
                score += 2;
                reasons.Add($"Pay: the posting starts at {Money(payMin)}, at or above your floor of {Money(floor)}.");
            }
            else if (application.PayMin is null && application.PayMax is null)
            {
                reasons.Add($"Pay: no pay range captured for this posting, so it can't be compared with your floor of {Money(floor)}.");
            }
            else
            {
                reasons.Add($"Pay: the range {Money(application.PayMin)}–{Money(application.PayMax)} overlaps your floor of {Money(floor)}.");
            }
        }

        // (e) Target-role keyword in the title or the posting text.
        var targetRoles = ParseList(profile.TargetRoles);
        if (targetRoles.Count > 0)
        {
            var matched = targetRoles.FirstOrDefault(keyword =>
                ContainsKeyword(application.RoleTitle, keyword)
                || (hasText && ContainsKeyword(postingText, keyword)));
            if (matched is not null)
            {
                score += 2;
                reasons.Add($"Target role match: “{matched}”.");
            }
            else
            {
                score -= 1;
                reasons.Add("No target-role keyword matched the role title or posting.");
            }
        }

        string verdict;
        if (blocked)
        {
            verdict = VerdictBlocked;
        }
        else if (!HasAnyCriteria(profile))
        {
            verdict = VerdictNotChecked;
            reasons.Add("Your search profile has no criteria yet — add target roles, must-haves, deal-breakers, or a pay floor for a sharper verdict.");
        }
        else
        {
            verdict = score >= 3 ? VerdictStrongFit
                : score >= 1 ? VerdictPossible
                : VerdictPoorFit;
        }

        return new FitCheckResult(verdict, reasons, score, blocked);
    }

    /// <summary>The profile's must-have keywords, each marked present/missing in the posting text (for the Prepare checklist).</summary>
    public List<MustHaveCheck> CheckMustHaves(SearchProfile? profile, string? postingText)
    {
        if (profile is null)
        {
            return new List<MustHaveCheck>();
        }

        var text = postingText ?? string.Empty;
        return ParseList(profile.MustHaves)
            .Select(keyword => new MustHaveCheck(keyword, ContainsKeyword(text, keyword)))
            .ToList();
    }

    /// <summary>Splits a comma/newline/semicolon-separated profile field into distinct keywords.</summary>
    public static List<string> ParseList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new List<string>();
        }

        return value
            .Split(new[] { '\n', '\r', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool HasAnyCriteria(SearchProfile profile) =>
        profile.PayFloor.HasValue
        || ParseList(profile.TargetRoles).Count > 0
        || ParseList(profile.MustHaves).Count > 0
        || ParseList(profile.DealBreakers).Count > 0
        || ParseList(profile.SkipCompanies).Count > 0;

    private static bool ContainsKeyword(string text, string keyword) =>
        !string.IsNullOrWhiteSpace(keyword)
        && text.Contains(keyword, StringComparison.OrdinalIgnoreCase);

    private static string Money(int? value) =>
        value.HasValue ? "$" + value.Value.ToString("N0") : "—";
}
