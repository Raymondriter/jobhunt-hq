namespace JobHuntHQ.Services;

/// <summary>
/// ATS platform detection from a posting URL, plus a short static "form map"
/// per platform (M4 phase 2, from the research pack's ATS table): what the
/// application form typically looks like, how long it takes, and the traps
/// an AI helper should warn about. Detection is host-based only — the URL is
/// never fetched here.
/// </summary>
public static class AtsInfo
{
    public const string UnknownKey = "unknown";

    /// <summary>Machine key for the detected platform: workday | greenhouse | lever | ashby | workable | icims | unknown.</summary>
    public static string DetectKey(string? postingUrl)
    {
        if (string.IsNullOrWhiteSpace(postingUrl)
            || !Uri.TryCreate(postingUrl.Trim(), UriKind.Absolute, out var uri))
        {
            return UnknownKey;
        }

        var host = uri.Host.ToLowerInvariant();
        if (HostIs(host, "myworkdayjobs.com") || host.Contains("myworkdayjobs") || HostIs(host, "workday.com"))
        {
            return "workday";
        }
        if (HostIs(host, "greenhouse.io"))
        {
            return "greenhouse";
        }
        if (HostIs(host, "lever.co"))
        {
            return "lever";
        }
        if (HostIs(host, "ashbyhq.com"))
        {
            return "ashby";
        }
        if (HostIs(host, "workable.com"))
        {
            return "workable";
        }
        if (HostIs(host, "icims.com"))
        {
            return "icims";
        }
        return UnknownKey;
    }

    public static string DisplayName(string key) => key switch
    {
        "workday" => "Workday",
        "greenhouse" => "Greenhouse",
        "lever" => "Lever",
        "ashby" => "Ashby",
        "workable" => "Workable",
        "icims" => "iCIMS",
        _ => "Unknown"
    };

    /// <summary>The form-map paragraph for a platform key, written for the AI packet.</summary>
    public static string FormMap(string key) => key switch
    {
        "workday" =>
            "Workday (detected from the posting URL). Expect a long form: roughly 73 fields across a 5–7 page wizard, " +
            "about 22–35 minutes. You need a separate Workday account for this employer — an account at one company " +
            "does not work at another. Be ready to re-enter every job with start/end month and year; the resume parser " +
            "often fails, so check every auto-filled field. Knockout questions to expect: work authorization, " +
            "sponsorship, relocation.",
        "greenhouse" =>
            "Greenhouse (detected from the posting URL). Expect 15–30 fields on 1–2 pages, about 6–9 minutes. No " +
            "account — the application is tied to your email address. Expect 2–5 custom free-text questions (e.g. " +
            "\"Why us?\") and a voluntary EEO self-identification block at the end. Employers can auto-reject based " +
            "on custom-question answers, so draft those carefully. Resume parsing fails on images, tables, " +
            "headers/footers, and multi-column layouts — a plain single-column resume parses best.",
        "lever" =>
            "Lever (detected from the posting URL). Expect a short single-page form, 8–20 fields, about 4–7 minutes. " +
            "Your full name goes in a single field. Uploading a resume auto-populates your profile — check what it " +
            "filled in. Knockout answers can trigger automatic archiving, so answer authorization and sponsorship " +
            "questions exactly as the standing answers say.",
        "ashby" =>
            "Ashby (detected from the posting URL). Expect about 10 fields on one page, 5–8 minutes; common at " +
            "startups. The cover letter is often an inline free-text field rather than a file upload. School and " +
            "degree names are standardised at submit time — pick the closest match rather than typing a variant.",
        "workable" =>
            "Workable (detected from the posting URL). Expect a short form in the Lever/Greenhouse pattern: contact " +
            "details, a resume upload, and a handful of custom questions, which often include work-authorization " +
            "and sponsorship knockouts. Have the standing answers ready and use them verbatim.",
        "icims" =>
            "iCIMS (detected from the posting URL). Expect long, enterprise-style forms (Taleo-like). The resume " +
            "parser is formatting-sensitive — a plain single-column resume with standard headings parses best. " +
            "Whether an account or profile can be reused varies by employer.",
        _ =>
            "The ATS platform could not be detected from the posting URL. Expect the universal pattern: contact " +
            "details, a resume upload, work-authorization and sponsorship questions (common knockouts — answer " +
            "only from the standing answers), and possibly custom free-text questions. A plain, single-column " +
            "resume with standard headings parses most reliably everywhere."
    };

    private static bool HostIs(string host, string domain) =>
        host == domain || host.EndsWith("." + domain, StringComparison.Ordinal);
}
