using JobHuntHQ.Models;
using JobHuntHQ.Services;

namespace JobHuntHQ.Tests;

public class EmailParserServiceTests
{
    [Fact]
    public void Parse_TeamsInviteWinsOverRejectionLanguage()
    {
        var raw =
            "From: Ada Lovelace <ada@analytical.example>\n" +
            "Subject: Your interview with Analytical Engines \u2013 Compiler Engineer\n" +
            "Join https://teams.microsoft.com/l/meetup-join/19%3ameeting.\n" +
            "Fri, Nov 27, 2026 at 9:00 AM EST\n" +
            "30 minutes\n" +
            "Meeting ID: 123 456 789\n" +
            "Passcode: abc12\n" +
            "You'll be meeting: Ada Lovelace, Principal\n" +
            "unfortunately the room changed\n";

        var parsed = EmailParserService.Parse(raw);

        Assert.Equal(ApplicationStatus.Interview, parsed.DetectedStatus);
        Assert.Equal("Analytical Engines", parsed.Company);
        Assert.Equal("Compiler Engineer", parsed.RoleTitle);
        Assert.NotNull(parsed.MeetingUrl);
        Assert.Contains("teams.microsoft.com", parsed.MeetingUrl, StringComparison.OrdinalIgnoreCase);
        Assert.False(parsed.MeetingUrl.EndsWith('.'));
        Assert.Equal(new DateTime(2026, 11, 27, 9, 0, 0), parsed.InterviewScheduledLocal);
        Assert.Equal(30, parsed.InterviewDurationMinutes);
        Assert.Equal("EST", parsed.TimeZoneLabel);
        Assert.NotNull(parsed.Interviewer);
        Assert.StartsWith("Ada Lovelace", parsed.Interviewer, StringComparison.Ordinal);
        Assert.Equal("Meeting ID: 123 456 789 | Passcode: abc12", parsed.ConfirmationReference);
        Assert.Null(parsed.DateApplied);
    }

    [Fact]
    public void Parse_RejectionWithoutInterviewCue()
    {
        var raw =
            "Subject: Northwind: application status update\n" +
            "Thank you for your interest in Northwind. We have decided to move forward with other candidates.\n";

        var parsed = EmailParserService.Parse(raw);
        var today = DateTime.Today.ToString("yyyy-MM-dd");

        Assert.Equal(ApplicationStatus.Rejected, parsed.DetectedStatus);
        Assert.Equal("Northwind", parsed.Company);
        Assert.Equal("Software Engineer", parsed.RoleTitle);
        Assert.Null(parsed.DateApplied);
        Assert.NotNull(parsed.ConfirmationReference);
        Assert.Contains(today, parsed.ConfirmationReference, StringComparison.Ordinal);
        Assert.DoesNotContain("Meeting ID", parsed.ConfirmationReference, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_AppliedConfirmationUsesToday()
    {
        var raw =
            "Subject: Thanks for applying to Contoso!\n" +
            "Your application for the Backend Engineer job was submitted successfully.\n";

        var parsed = EmailParserService.Parse(raw);

        Assert.Equal(ApplicationStatus.Applied, parsed.DetectedStatus);
        Assert.Equal("Contoso", parsed.Company);
        Assert.Equal("Backend Engineer", parsed.RoleTitle);
        Assert.Equal(DateOnly.FromDateTime(DateTime.Today), parsed.DateApplied);
    }

    [Fact]
    public void Parse_OverridesReplaceExtractedCompanyAndRole()
    {
        var raw =
            "Subject: Thanks for applying to Contoso!\n" +
            "Your application for the Backend Engineer job was submitted successfully.\n";

        var parsed = EmailParserService.Parse(raw, "  Override Co  ", "\tOverride Role\t");

        Assert.Equal("Override Co", parsed.Company);
        Assert.Equal("Override Role", parsed.RoleTitle);
    }

    [Fact]
    public void Parse_NullOrWhitespaceDoesNotThrow()
    {
        var fromNull = EmailParserService.Parse(null!);
        var fromBlank = EmailParserService.Parse(" \r\n\t ");

        foreach (var parsed in new[] { fromNull, fromBlank })
        {
            Assert.Equal(ApplicationStatus.Applied, parsed.DetectedStatus);
            Assert.Equal("Software Engineer", parsed.RoleTitle);
            Assert.Equal(string.Empty, parsed.Company);
        }
    }

    [Fact]
    public void Parse_PrefersZoomJoinLinkOverUnrelatedUrl()
    {
        var chosen = EmailParserService.Parse(
            "See https://example.com/jobs and https://zoom.us/j/9876543210 for details.");
        Assert.NotNull(chosen.MeetingUrl);
        Assert.Contains("zoom.us/j/", chosen.MeetingUrl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("example.com", chosen.MeetingUrl, StringComparison.OrdinalIgnoreCase);

        var notJoin = EmailParserService.Parse(
            "Profile page https://zoom.us/my/ada and https://example.com/about.");
        Assert.Null(notJoin.MeetingUrl);
    }

    [Fact]
    public void Parse_PhoneScreenWithoutInterviewPhraseStaysApplied()
    {
        var phone = EmailParserService.Parse("phone screen next week");
        Assert.Equal(ApplicationStatus.Applied, phone.DetectedStatus);

        var recruiter = EmailParserService.Parse("Please book a recruiter screen");
        Assert.Equal(ApplicationStatus.Interview, recruiter.DetectedStatus);
    }
}
