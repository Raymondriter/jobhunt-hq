using System.Text.RegularExpressions;
using JobHuntHQ.Models;

namespace JobHuntHQ.Services;

public sealed record ResumeMatchReport(
    bool HasPostingText,
    bool HasResumeText,
    IReadOnlyList<string> MatchedSkills,
    IReadOnlyList<string> MissingSkills,
    int? MatchPercent);

public sealed record StandingAnswerCard(string Label, string Value);

/// <summary>
/// Pure deterministic, zero-cost ($0) preparation intelligence built into
/// JobHunt HQ so users get immediate resume-vs-posting gap analysis,
/// auto-ranked STAR stories, and 1-click standing answer cards without needing
/// to copy/paste into an external AI or pay for hosted LLMs.
/// </summary>
public static class PrepAnalysisService
{
    private static readonly (string Display, string Pattern)[] SkillLexicon =
    {
        ("C#", @"(?<![A-Za-z0-9_])c#(?![A-Za-z0-9_])"),
        (".NET", @"(?<![A-Za-z0-9_])(?:\.net|dotnet)(?:\s*(?:core|framework|\d+))?(?![A-Za-z0-9_])"),
        ("ASP.NET Core", @"(?<![A-Za-z0-9_])asp\.net(?:\s*core|\s*mvc)?(?![A-Za-z0-9_])"),
        ("Entity Framework", @"(?<![A-Za-z0-9_])(?:entity\s+framework|ef\s*core|ef6)(?![A-Za-z0-9_])"),
        ("Blazor", @"\bblazor\b"),
        ("SQL Server", @"\b(?:sql\s*server|t-sql|tsql|mssql)\b"),
        ("SQL", @"\bsql\b"),
        ("PostgreSQL", @"\b(?:postgresql|postgres)\b"),
        ("MySQL", @"\bmysql\b"),
        ("MongoDB", @"\bmongodb\b"),
        ("Redis", @"\bredis\b"),
        ("TypeScript", @"\btypescript\b"),
        ("JavaScript", @"\bjavascript\b"),
        ("React", @"\breact(?:\.?js)?\b"),
        ("Next.js", @"\bnext\.?js\b"),
        ("Vue", @"\bvue(?:\.?js|\s*3)?\b"),
        ("Angular", @"\bangular\b"),
        ("Node.js", @"\bnode\.?js\b"),
        ("Python", @"\bpython\b"),
        ("Java", @"\bjava\b"),
        ("Go", @"\b(?:golang|go\s+language)\b"),
        ("Rust", @"\brust\b"),
        ("Ruby", @"\bruby(?:\s+on\s+rails)?\b"),
        ("PHP", @"\bphp\b"),
        ("HTML/CSS", @"\b(?:html5?|css3?|scss|sass)\b"),
        ("Tailwind", @"\btailwind\b"),
        ("Azure", @"\bazure\b"),
        ("AWS", @"\b(?:aws|amazon\s+web\s+services)\b"),
        ("GCP", @"\b(?:gcp|google\s+cloud)\b"),
        ("Docker", @"\bdocker\b"),
        ("Kubernetes", @"\b(?:kubernetes|k8s)\b"),
        ("Terraform", @"\bterraform\b"),
        ("CI/CD", @"(?<![A-Za-z0-9_])ci\s*/\s*cd(?![A-Za-z0-9_])|\b(?:github\s+actions|azure\s+devops|teamcity|octopus\s+deploy|jenkins)\b"),
        ("REST APIs", @"\b(?:rest(?:ful)?\s*apis?|web\s*api)\b"),
        ("GraphQL", @"\bgraphql\b"),
        ("gRPC", @"\bgrpc\b"),
        ("Microservices", @"\bmicroservices?\b"),
        ("Kafka", @"\bkafka\b"),
        ("RabbitMQ", @"\brabbitmq\b"),
        ("Service Bus / Queues", @"\b(?:service\s+bus|sqs|sns|message\s+queue)\b"),
        ("OAuth / SSO", @"\b(?:oauth2?|oidc|openid|saml|sso|entra|active\s+directory)\b"),
        ("Unit Testing", @"\b(?:xunit|nunit|mstest|jest|vitest|unit\s+test(?:ing|s)?)\b"),
        ("Playwright / Cypress", @"\b(?:playwright|cypress|selenium)\b"),
        ("Git", @"\bgit(?:hub|lab)?\b"),
        ("Linux", @"\blinux\b"),
        ("Agile / Scrum", @"\b(?:agile|scrum|kanban)\b"),
        ("AI / LLM Tools", @"\b(?:llm|llms|ai-first|generative\s+ai|copilot|agentic)\b"),
        ("Distributed Systems", @"\bdistributed\s+systems?\b"),
        ("Multi-Tenant SaaS", @"\b(?:multi-?tenant|saas)\b"),
        ("System Design", @"\bsystem\s+design\b"),
        ("Payments / Billing", @"\b(?:payments?|billing|invoicing|payroll|accounting|fintech)\b")
    };

    public static ResumeMatchReport AnalyzeResumeMatch(
        JobApplication app,
        SearchProfile? profile,
        string? resumeText)
    {
        var postingCorpus = $"{app.RoleTitle}\n{app.PostingText ?? string.Empty}".Trim();
        var hasPosting = !string.IsNullOrWhiteSpace(app.PostingText) || !string.IsNullOrWhiteSpace(app.RoleTitle);
        var hasResume = !string.IsNullOrWhiteSpace(resumeText);

        if (!hasPosting)
        {
            return new ResumeMatchReport(false, hasResume, Array.Empty<string>(), Array.Empty<string>(), null);
        }

        var postingSkills = new List<(string Display, string Pattern)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Check user's own profile MustHaves first if present in the posting.
        foreach (var kw in FitCheckService.ParseList(profile?.MustHaves))
        {
            var pat = $@"(?<![A-Za-z0-9_]){Regex.Escape(kw)}(?![A-Za-z0-9_])";
            if (Regex.IsMatch(postingCorpus, pat, RegexOptions.IgnoreCase) && seen.Add(kw))
            {
                postingSkills.Add((kw, pat));
            }
        }

        // 2. Check curated skill lexicon against posting text + role title.
        foreach (var (display, pattern) in SkillLexicon)
        {
            if (seen.Contains(display)) continue;
            if (Regex.IsMatch(postingCorpus, pattern, RegexOptions.IgnoreCase) && seen.Add(display))
            {
                postingSkills.Add((display, pattern));
            }
        }

        if (!hasResume || postingSkills.Count == 0)
        {
            // When no resume PDF text is available yet, list detected posting skills under Missing so the user sees what the posting asks for.
            return new ResumeMatchReport(
                hasPosting,
                hasResume,
                Array.Empty<string>(),
                postingSkills.Select(s => s.Display).ToList(),
                null);
        }

        var matched = new List<string>();
        var missing = new List<string>();
        foreach (var (display, pattern) in postingSkills)
        {
            if (Regex.IsMatch(resumeText!, pattern, RegexOptions.IgnoreCase))
            {
                matched.Add(display);
            }
            else
            {
                missing.Add(display);
            }
        }

        var pct = (int)Math.Round(100.0 * matched.Count / postingSkills.Count);
        return new ResumeMatchReport(hasPosting, true, matched, missing, pct);
    }

    public static List<StoryBankItem> RankStories(
        IReadOnlyList<StoryBankItem> stories,
        HashSet<int> matchedStoryIds,
        JobApplication app)
    {
        var corpus = $"{app.Company} {app.RoleTitle} {app.PostingText ?? string.Empty}".ToLowerInvariant();
        return stories
            .Select(s =>
            {
                var score = matchedStoryIds.Contains(s.Id) ? 10 : 0;
                if (!string.IsNullOrWhiteSpace(s.Tags))
                {
                    foreach (var tag in s.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (tag.Length >= 2 && corpus.Contains(tag.ToLowerInvariant(), StringComparison.Ordinal))
                        {
                            score += 3;
                        }
                    }
                }
                return (Story: s, Score: score);
            })
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Story.UpdatedUtc)
            .Select(x => x.Story)
            .ToList();
    }

    public static List<StandingAnswerCard> ParseStandingAnswers(string? standingAnswers)
    {
        var cards = new List<StandingAnswerCard>();
        if (string.IsNullOrWhiteSpace(standingAnswers))
        {
            return cards;
        }

        var lines = standingAnswers.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var raw in lines)
        {
            var line = raw.TrimStart('-', '*', '•', ' ');
            if (string.IsNullOrWhiteSpace(line)) continue;

            var colon = line.IndexOf(':');
            if (colon > 1 && colon < 48)
            {
                var label = line[..colon].Trim();
                var value = line[(colon + 1)..].Trim();
                if (!string.IsNullOrWhiteSpace(label) && !string.IsNullOrWhiteSpace(value))
                {
                    cards.Add(new StandingAnswerCard(label, value));
                    continue;
                }
            }

            cards.Add(new StandingAnswerCard("Standing note", line));
        }

        return cards;
    }

    /// <summary>
    /// Extracts real, deterministic technical skills from a user's uploaded resume
    /// PDF text so first-time onboarding can auto-fill their Search Profile in one click ($0).
    /// </summary>
    public static List<string> ExtractResumeSkills(string? resumeText, int maxSkills = 12)
    {
        if (string.IsNullOrWhiteSpace(resumeText))
        {
            return new List<string>();
        }

        var found = new List<string>();
        foreach (var (display, pattern) in SkillLexicon)
        {
            if (Regex.IsMatch(resumeText, pattern, RegexOptions.IgnoreCase))
            {
                found.Add(display);
                if (found.Count >= maxSkills) break;
            }
        }
        return found;
    }

    /// <summary>
    /// Suggests target role titles grounded strictly in keywords present in the
    /// user's own uploaded resume text.
    /// </summary>
    public static List<string> SuggestTargetRoles(string? resumeText)
    {
        if (string.IsNullOrWhiteSpace(resumeText))
        {
            return new List<string>();
        }

        var roles = new List<string>();
        var lower = resumeText.ToLowerInvariant();
        var isSenior = lower.Contains("senior") || lower.Contains("lead") || lower.Contains("staff") || lower.Contains("architect");
        var prefix = isSenior ? "Senior " : "";

        if (Regex.IsMatch(resumeText, @"\b(full[\s\-]?stack)\b", RegexOptions.IgnoreCase))
            roles.Add($"{prefix}Full-Stack Software Engineer");
        if (Regex.IsMatch(resumeText, @"(?<![A-Za-z0-9_])(\.net|c#|asp\.net)(?![A-Za-z0-9_])", RegexOptions.IgnoreCase))
            roles.Add($"{prefix}.NET Software Engineer");
        if (Regex.IsMatch(resumeText, @"\b(back[\s\-]?end|rest\s*api|microservices)\b", RegexOptions.IgnoreCase))
            roles.Add($"{prefix}Backend Engineer");
        if (Regex.IsMatch(resumeText, @"\b(front[\s\-]?end|react|vue|angular)\b", RegexOptions.IgnoreCase) && roles.Count < 3)
            roles.Add($"{prefix}Frontend Engineer");
        if (Regex.IsMatch(resumeText, @"\b(devops|sre|kubernetes|terraform)\b", RegexOptions.IgnoreCase) && roles.Count < 3)
            roles.Add($"{prefix}DevOps / Cloud Engineer");
        if (Regex.IsMatch(resumeText, @"\b(data\s+engineer|spark|bigquery|snowflake)\b", RegexOptions.IgnoreCase) && roles.Count < 3)
            roles.Add($"{prefix}Data Engineer");

        if (roles.Count == 0)
        {
            roles.Add($"{prefix}Software Engineer");
        }

        return roles.Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToList();
    }
}
