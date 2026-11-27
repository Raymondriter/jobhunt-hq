using JobHuntHQ.Models;
using JobHuntHQ.Services;

namespace JobHuntHQ.Tests;

public class FitCheckServiceTests
{
    private readonly FitCheckService _sut = new();

    [Fact]
    public void Evaluate_without_a_profile_is_not_checked()
    {
        var result = _sut.Evaluate(App("Acme", "Engineer"), null);

        Assert.Equal(FitCheckService.VerdictNotChecked, result.Verdict);
        Assert.False(result.Blocked);
        Assert.Equal(0, result.Score);
    }

    [Fact]
    public void Evaluate_with_no_criteria_is_not_checked()
    {
        var result = _sut.Evaluate(App("Acme", "Engineer", posting: "C# role"), new SearchProfile());

        Assert.Equal(FitCheckService.VerdictNotChecked, result.Verdict);
        Assert.False(result.Blocked);
    }

    [Fact]
    public void Evaluate_blocks_a_company_whose_name_contains_a_skip_entry()
    {
        var profile = new SearchProfile { SkipCompanies = "acme" };
        var result = _sut.Evaluate(App("Acme Corp", "Engineer", posting: "great role"), profile);

        Assert.Equal(FitCheckService.VerdictBlocked, result.Verdict);
        Assert.True(result.Blocked);
        Assert.Contains(result.Reasons, reason => reason.Contains("Acme Corp", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_blocks_when_posting_text_contains_a_deal_breaker()
    {
        var profile = new SearchProfile
        {
            DealBreakers = "clearance",
            MustHaves = "csharp",
            TargetRoles = "engineer"
        };
        var result = _sut.Evaluate(
            App("Northwind", "Engineer", posting: "Requires an active Clearance and CSharp."),
            profile);

        Assert.Equal(FitCheckService.VerdictBlocked, result.Verdict);
        Assert.True(result.Blocked);
        Assert.Contains(result.Reasons, reason => reason.Contains("clearance", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_scores_pay_must_haves_and_target_roles_into_a_verdict()
    {
        var profile = new SearchProfile
        {
            PayFloor = 100_000,
            MustHaves = "csharp; azure",
            TargetRoles = "staff engineer"
        };
        var application = App(
            "Contoso",
            "Staff Engineer",
            posting: "We use CSharp every day.",
            payMin: 120_000,
            payMax: 160_000);

        var result = _sut.Evaluate(application, profile);

        // +2 pay floor met, +1 csharp, -1 azure missing, +2 target role = 4.
        Assert.Equal(4, result.Score);
        Assert.False(result.Blocked);
        Assert.Equal(FitCheckService.VerdictStrongFit, result.Verdict);
        Assert.Contains(result.Reasons, reason => reason.Contains("azure", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(90_000, null, -2)]
    [InlineData(null, 130_000, 2)]
    [InlineData(140_000, 80_000, 0)]
    [InlineData(null, null, 0)]
    public void Evaluate_compares_posting_pay_with_the_floor(int? payMax, int? payMin, int expectedScore)
    {
        var profile = new SearchProfile { PayFloor = 100_000 };
        var result = _sut.Evaluate(App("Contoso", "Engineer", payMin: payMin, payMax: payMax), profile);

        Assert.Equal(expectedScore, result.Score);
        Assert.Equal(
            expectedScore >= 3 ? FitCheckService.VerdictStrongFit
                : expectedScore >= 1 ? FitCheckService.VerdictPossible
                : FitCheckService.VerdictPoorFit,
            result.Verdict);
    }

    [Fact]
    public void Evaluate_skips_keyword_rules_when_posting_text_is_missing()
    {
        var profile = new SearchProfile { MustHaves = "csharp", DealBreakers = "clearance" };
        var result = _sut.Evaluate(App("Contoso", "Engineer"), profile);

        Assert.Equal(0, result.Score);
        Assert.False(result.Blocked);
        Assert.Contains(result.Reasons, reason => reason.Contains("No posting text", StringComparison.Ordinal));
    }

    [Fact]
    public void ParseList_splits_trims_and_dedupes_case_insensitively()
    {
        var values = FitCheckService.ParseList(" CSharp,\ncsharp; Azure \r\n");

        Assert.Equal(new[] { "CSharp", "Azure" }, values);
    }

    [Fact]
    public void CheckMustHaves_marks_keywords_present_in_the_posting()
    {
        var profile = new SearchProfile { MustHaves = "csharp, kubernetes" };

        var checks = _sut.CheckMustHaves(profile, "Daily CSharp work");

        Assert.Equal(2, checks.Count);
        Assert.True(checks[0].IsPresent);
        Assert.False(checks[1].IsPresent);
        Assert.Empty(_sut.CheckMustHaves(null, "anything"));
    }

    private static JobApplication App(
        string company,
        string title,
        string? posting = null,
        int? payMin = null,
        int? payMax = null) =>
        new()
        {
            UserId = "user",
            Company = company,
            RoleTitle = title,
            PostingText = posting,
            PayMin = payMin,
            PayMax = payMax
        };
}
