using JobHuntHQ.Models;
using JobHuntHQ.Services;

namespace JobHuntHQ.Tests;

public class PrepAnalysisServiceTests
{
    [Fact]
    public void Analyze_NoPostingTextAndBlankRole_HasNoSkills()
    {
        var report = PrepAnalysisService.AnalyzeResumeMatch(
            App(role: "   ", posting: "  "),
            profile: null,
            resumeText: null);

        Assert.False(report.HasPostingText);
        Assert.Null(report.MatchPercent);
        Assert.Empty(report.MatchedSkills);
        Assert.Empty(report.MissingSkills);
    }

    [Fact]
    public void Analyze_RoleWithoutResume_ListsPostingSkillsAsMissing()
    {
        var report = PrepAnalysisService.AnalyzeResumeMatch(
            App(role: "React engineer", posting: null),
            profile: null,
            resumeText: null);

        Assert.True(report.HasPostingText);
        Assert.False(report.HasResumeText);
        Assert.Empty(report.MatchedSkills);
        Assert.Contains("React", report.MissingSkills);
        Assert.Null(report.MatchPercent);
    }

    [Fact]
    public void Analyze_SqlServerAlsoMatchesTheSqlLexiconEntry()
    {
        var onlyServer = PrepAnalysisService.AnalyzeResumeMatch(
            App(role: "Engineer", posting: "SQL Server"),
            profile: null,
            resumeText: "I bake bread");
        Assert.Contains("SQL Server", onlyServer.MissingSkills);
        Assert.Contains("SQL", onlyServer.MissingSkills);

        var report = PrepAnalysisService.AnalyzeResumeMatch(
            App(role: "Engineer", posting: "We need C#, SQL Server, and Kubernetes."),
            profile: null,
            resumeText: "I ship C# and SQL Server.");

        // \bsql\b matches inside "SQL Server", on the posting and on the resume,
        // so SQL is a third matched skill beside C# and SQL Server. Kubernetes is the miss.
        Assert.Equal(new[] { "C#", "SQL Server", "SQL" }, report.MatchedSkills);
        Assert.Equal(new[] { "Kubernetes" }, report.MissingSkills);
        Assert.Equal(75, report.MatchPercent);

        Assert.DoesNotContain("Java", PrepAnalysisService.ExtractResumeSkills("javascript"));
        Assert.Contains("JavaScript", PrepAnalysisService.ExtractResumeSkills("javascript"));
        Assert.DoesNotContain("Go", PrepAnalysisService.ExtractResumeSkills("I wrote go code"));
        Assert.Contains("Go", PrepAnalysisService.ExtractResumeSkills("golang"));
        Assert.DoesNotContain("C#", PrepAnalysisService.ExtractResumeSkills("C++"));
    }

    [Fact]
    public void Analyze_ProfileMustHaveIncludedOnlyWhenPostingContainsIt()
    {
        var profile = new SearchProfile { UserId = "user-a", MustHaves = "F#, COBOL" };
        var report = PrepAnalysisService.AnalyzeResumeMatch(
            App(role: "Engineer", posting: "use F# daily"),
            profile,
            resumeText: "I write F# in production");

        Assert.Contains("F#", report.MatchedSkills);
        Assert.DoesNotContain("COBOL", report.MatchedSkills.Concat(report.MissingSkills));
    }

    [Fact]
    public void ExtractResumeSkills_RespectsLexiconOrderAndCap()
    {
        Assert.Empty(PrepAnalysisService.ExtractResumeSkills(null));
        Assert.Empty(PrepAnalysisService.ExtractResumeSkills(string.Empty));
        Assert.Empty(PrepAnalysisService.ExtractResumeSkills("   "));

        Assert.Equal(new[] { "Python" }, PrepAnalysisService.ExtractResumeSkills("Azure and Python", 1));
        // The cap is checked after the skill is added, so a non-positive max still keeps the first hit.
        Assert.Equal(new[] { "Python" }, PrepAnalysisService.ExtractResumeSkills("Azure and Python", 0));
        Assert.Equal(new[] { "Python" }, PrepAnalysisService.ExtractResumeSkills("Azure and Python", -5));
    }

    [Fact]
    public void SuggestTargetRoles_UsesResumeKeywordsAndSeniorPrefix()
    {
        Assert.Empty(PrepAnalysisService.SuggestTargetRoles(null));
        Assert.Empty(PrepAnalysisService.SuggestTargetRoles(" \n\t "));

        var devops = PrepAnalysisService.SuggestTargetRoles("I lead platform work with kubernetes and terraform");
        Assert.Contains("Senior DevOps / Cloud Engineer", devops);

        var stacked = PrepAnalysisService.SuggestTargetRoles("full-stack C# backend react services");
        Assert.True(stacked.Count <= 3);
        Assert.Contains(stacked, role => role.Contains("Full-Stack", StringComparison.Ordinal));
        Assert.Contains(stacked, role => role.Contains(".NET", StringComparison.Ordinal));
        Assert.Contains(stacked, role => role.Contains("Backend", StringComparison.Ordinal));
        Assert.DoesNotContain(stacked, role => role.Contains("Frontend", StringComparison.Ordinal));

        Assert.Equal(
            new[] { "Software Engineer" },
            PrepAnalysisService.SuggestTargetRoles("I enjoy gardening and teaching"));

        var senior = PrepAnalysisService.SuggestTargetRoles("senior full-stack and backend generalist");
        Assert.NotEmpty(senior);
        Assert.All(senior, role => Assert.StartsWith("Senior ", role, StringComparison.Ordinal));
    }

    [Fact]
    public void ParseStandingAnswers_SplitsLabelsAndFallsBackToNotes()
    {
        Assert.Empty(PrepAnalysisService.ParseStandingAnswers(null));
        Assert.Empty(PrepAnalysisService.ParseStandingAnswers(string.Empty));
        Assert.Empty(PrepAnalysisService.ParseStandingAnswers(" \n\t "));

        var work = Assert.Single(PrepAnalysisService.ParseStandingAnswers("- Work auth: US citizen"));
        Assert.Equal("Work auth", work.Label);
        Assert.Equal("US citizen", work.Value);

        var note = Assert.Single(PrepAnalysisService.ParseStandingAnswers("Bring a paper notebook"));
        Assert.Equal("Standing note", note.Label);
        Assert.Equal("Bring a paper notebook", note.Value);

        var longLine = new string('L', 48) + ": still a note";
        var longCard = Assert.Single(PrepAnalysisService.ParseStandingAnswers(longLine));
        Assert.Equal("Standing note", longCard.Label);
        Assert.Equal(longLine, longCard.Value);

        var bullets = PrepAnalysisService.ParseStandingAnswers("* Visa: yes\n\u2022 Location: remote");
        Assert.Equal(2, bullets.Count);
        Assert.Equal("Visa", bullets[0].Label);
        Assert.Equal("yes", bullets[0].Value);
        Assert.Equal("Location", bullets[1].Label);
        Assert.Equal("remote", bullets[1].Value);
    }

    [Fact]
    public void RankStories_PrefersMatchedIdsThenTagsThenRecency()
    {
        var oldest = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var older = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var newer = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var newest = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var matched = Story(1, "Matched id", "n", oldest);
        var tagHit = Story(2, "Company tag", "NoRtHwInD", oldest);
        var shortTag = Story(3, "One letter", "n", newest);
        var tieNewer = Story(5, "Newer equal", null, newer);
        var tieOlder = Story(4, "Older equal", null, older);

        var ranked = PrepAnalysisService.RankStories(
            new[] { tieOlder, tagHit, shortTag, matched, tieNewer },
            new HashSet<int> { matched.Id },
            App(role: "Compiler Engineer", posting: "distributed systems", company: "Northwind"));

        Assert.Equal(
            new[] { matched.Id, tagHit.Id, shortTag.Id, tieNewer.Id, tieOlder.Id },
            ranked.Select(story => story.Id).ToArray());

        var roleRank = PrepAnalysisService.RankStories(
            new[] { Story(1, "Role tag", "COMPILER", oldest), Story(2, "None", null, newest) },
            new HashSet<int>(),
            App(role: "Compiler Engineer", posting: "nothing relevant", company: "Acme"));
        Assert.Equal(1, roleRank[0].Id);

        var postingRank = PrepAnalysisService.RankStories(
            new[] { Story(1, "Posting tag", "DISTRIBUTED", oldest), Story(2, "None", null, newest) },
            new HashSet<int>(),
            App(role: "Engineer", posting: "distributed systems", company: "Acme"));
        Assert.Equal(1, postingRank[0].Id);
    }

    private static JobApplication App(string role, string? posting, string company = "Acme") =>
        new()
        {
            UserId = "user-a",
            Company = company,
            RoleTitle = role,
            PostingText = posting
        };

    private static StoryBankItem Story(int id, string title, string? tags, DateTime updatedUtc) =>
        new()
        {
            Id = id,
            UserId = "user-a",
            Title = title,
            Tags = tags,
            UpdatedUtc = updatedUtc
        };
}
