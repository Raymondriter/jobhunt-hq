using JobHuntHQ.Models;
using JobHuntHQ.Services;

namespace JobHuntHQ.Tests;

public class CalendarFeedTests
{
    [Fact]
    public void ResolveIanaTimeZone_MapsLabelsAndPrefersEmbeddedIana()
    {
        Assert.Null(InterviewPacketService.ResolveIanaTimeZone(null));
        Assert.Null(InterviewPacketService.ResolveIanaTimeZone(string.Empty));
        Assert.Null(InterviewPacketService.ResolveIanaTimeZone("   "));
        Assert.Null(InterviewPacketService.ResolveIanaTimeZone("Kitchen"));

        Assert.Equal("America/Los_Angeles", InterviewPacketService.ResolveIanaTimeZone("PT"));
        Assert.Equal("America/Los_Angeles", InterviewPacketService.ResolveIanaTimeZone("PDT"));
        Assert.Equal("America/New_York", InterviewPacketService.ResolveIanaTimeZone("EST"));
        Assert.Equal("America/New_York", InterviewPacketService.ResolveIanaTimeZone("Eastern Time"));
        Assert.Equal("America/Chicago", InterviewPacketService.ResolveIanaTimeZone("CT"));
        Assert.Equal("America/Denver", InterviewPacketService.ResolveIanaTimeZone("MST"));
        Assert.Equal("UTC", InterviewPacketService.ResolveIanaTimeZone("UTC"));
        Assert.Equal("UTC", InterviewPacketService.ResolveIanaTimeZone("GMT"));
        Assert.Equal("Europe/London", InterviewPacketService.ResolveIanaTimeZone("BST"));
        Assert.Equal("America/New_York", InterviewPacketService.ResolveIanaTimeZone("EST (America/New_York)"));
    }

    [Fact]
    public void BuildGoogleCalendarUrl_BuildsTemplateOrReturnsNull()
    {
        var app = new JobApplication
        {
            UserId = "user-a",
            Company = "Acme & Co",
            RoleTitle = "C# Engineer"
        };
        var round = new InterviewRound
        {
            UserId = "user-a",
            RoundType = "Recruiter Screen",
            RoundNumber = 1,
            ScheduledLocal = null,
            DurationMinutes = 30,
            MeetingUrl = "https://teams.microsoft.com/l/meetup-join/abc",
            TimeZoneLabel = "PT"
        };

        Assert.Null(InterviewPacketService.BuildGoogleCalendarUrl(app, round, null));

        round.ScheduledLocal = new DateTime(2026, 11, 27, 9, 0, 0);
        var url = InterviewPacketService.BuildGoogleCalendarUrl(app, round, null);
        Assert.NotNull(url);
        Assert.StartsWith("https://calendar.google.com/calendar/render?", url, StringComparison.Ordinal);
        Assert.Contains("action=TEMPLATE", url, StringComparison.Ordinal);
        Assert.Contains("dates=20261127T090000/20261127T093000", url, StringComparison.Ordinal);
        Assert.Contains("ctz=" + Uri.EscapeDataString("America/Los_Angeles"), url, StringComparison.Ordinal);
        Assert.Contains("location=" + Uri.EscapeDataString(round.MeetingUrl), url, StringComparison.Ordinal);
        Assert.Contains(
            "text=" + Uri.EscapeDataString("Interview: Acme & Co \u2014 C# Engineer"),
            url,
            StringComparison.Ordinal);

        round.DurationMinutes = 0;
        round.ScheduledLocal = new DateTime(2026, 1, 15, 14, 0, 0);
        var zeroDuration = InterviewPacketService.BuildGoogleCalendarUrl(app, round, null);
        Assert.NotNull(zeroDuration);
        Assert.Contains("dates=20260115T140000/20260115T144500", zeroDuration, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCalendarFeed_OmitsUnscheduledAndEmitsTimedRoundWithAlarm()
    {
        var app = new JobApplication
        {
            Id = 10,
            UserId = "user-a",
            Company = "SchedCo",
            RoleTitle = "Engineer",
            Status = ApplicationStatus.Interview,
            InterviewRounds =
            {
                new InterviewRound
                {
                    Id = 101,
                    UserId = "user-a",
                    RoundNumber = 1,
                    RoundType = "Recruiter Screen",
                    ScheduledLocal = null,
                    MeetingUrl = "https://meet.example/unscheduled-only",
                    DurationMinutes = 30
                },
                new InterviewRound
                {
                    Id = 102,
                    UserId = "user-a",
                    RoundNumber = 2,
                    RoundType = "Technical",
                    ScheduledLocal = new DateTime(2026, 11, 27, 9, 0, 0),
                    DurationMinutes = 30,
                    TimeZoneLabel = "PT",
                    Completed = false,
                    MeetingUrl = "https://meet.example/live"
                }
            }
        };

        var feed = Feed(app);
        Assert.Equal(1, EventCount(feed));
        Assert.Contains("UID:jobhunthq-app-10-round-102@jobhunthq", feed, StringComparison.Ordinal);
        Assert.DoesNotContain("round-101", feed, StringComparison.Ordinal);
        Assert.DoesNotContain("unscheduled-only", feed, StringComparison.Ordinal);
        Assert.Contains("DTSTART;TZID=America/Los_Angeles:20261127T090000", feed, StringComparison.Ordinal);
        Assert.Contains("DTEND;TZID=America/Los_Angeles:20261127T093000", feed, StringComparison.Ordinal);
        Assert.Contains("BEGIN:VALARM", feed, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCalendarFeed_CompletedRoundHasCheckmarkAndNoAlarm()
    {
        var app = new JobApplication
        {
            Id = 11,
            UserId = "user-a",
            Company = "DoneCo",
            RoleTitle = "Engineer",
            Status = ApplicationStatus.Interview,
            InterviewRounds =
            {
                new InterviewRound
                {
                    Id = 111,
                    UserId = "user-a",
                    RoundNumber = 1,
                    RoundType = "Hiring Manager",
                    ScheduledLocal = new DateTime(2026, 6, 1, 10, 0, 0),
                    DurationMinutes = 0,
                    Completed = true
                }
            }
        };

        var feed = Feed(app);
        var ev = SingleEvent(feed);
        Assert.Contains("\u2713 ", ev, StringComparison.Ordinal);
        Assert.DoesNotContain("VALARM", ev, StringComparison.Ordinal);
        Assert.Contains("DTSTART:20260601T100000", ev, StringComparison.Ordinal);
        Assert.Contains("DTEND:20260601T104500", ev, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCalendarFeed_EscapesCommaSemicolonAndNewline()
    {
        var app = new JobApplication
        {
            Id = 40,
            UserId = "user-a",
            Company = "Acme, Inc; Labs",
            RoleTitle = "Engineer",
            Status = ApplicationStatus.Interview,
            InterviewRounds =
            {
                new InterviewRound
                {
                    Id = 7,
                    UserId = "user-a",
                    RoundNumber = 1,
                    RoundType = "Panel\nDeep dive",
                    ScheduledLocal = new DateTime(2026, 5, 1, 15, 0, 0),
                    DurationMinutes = 45
                }
            }
        };

        var feed = Feed(app);
        var summary = feed.Split("\r\n").Single(line => line.StartsWith("SUMMARY:", StringComparison.Ordinal));
        Assert.DoesNotContain("\n", summary);
        Assert.Contains(@"Acme\, Inc\; Labs", summary, StringComparison.Ordinal);
        Assert.Contains(@"Panel\nDeep dive", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCalendarFeed_FollowUpsRespectStatusAndNextAction()
    {
        var due = new DateOnly(2026, 3, 9);
        var offerDue = new DateOnly(2026, 4, 1);
        var feed = Feed(
            FollowUp(20, "OpenCo", ApplicationStatus.Applied, due, "Send thanks"),
            FollowUp(21, "RejectedCo", ApplicationStatus.Rejected, due, "ping rejected"),
            FollowUp(22, "GhostedCo", ApplicationStatus.Ghosted, due, "ping ghosted"),
            FollowUp(23, "WithdrawnCo", ApplicationStatus.Withdrawn, due, "ping withdrawn"),
            FollowUp(24, "BlankActionCo", ApplicationStatus.Applied, due, "   "),
            FollowUp(25, "OfferCo", ApplicationStatus.Offer, offerDue, "Sign offer"));

        Assert.Equal(2, EventCount(feed));
        Assert.Contains("UID:jobhunthq-app-20-followup-20260309@jobhunthq", feed, StringComparison.Ordinal);
        Assert.Contains("followup", feed, StringComparison.Ordinal);
        Assert.Contains("DTSTART;VALUE=DATE:20260309", feed, StringComparison.Ordinal);
        Assert.Contains("DTEND;VALUE=DATE:20260310", feed, StringComparison.Ordinal);
        Assert.Contains("Follow-up: OpenCo", feed, StringComparison.Ordinal);

        Assert.DoesNotContain("RejectedCo", feed, StringComparison.Ordinal);
        Assert.DoesNotContain("GhostedCo", feed, StringComparison.Ordinal);
        Assert.DoesNotContain("WithdrawnCo", feed, StringComparison.Ordinal);
        Assert.DoesNotContain("BlankActionCo", feed, StringComparison.Ordinal);
        Assert.DoesNotContain("ping rejected", feed, StringComparison.Ordinal);

        Assert.Contains("UID:jobhunthq-app-25-followup-20260401@jobhunthq", feed, StringComparison.Ordinal);
        Assert.Contains("DTSTART;VALUE=DATE:20260401", feed, StringComparison.Ordinal);
        Assert.Contains("DTEND;VALUE=DATE:20260402", feed, StringComparison.Ordinal);
        Assert.Contains("Follow-up: OfferCo", feed, StringComparison.Ordinal);
    }

    private static JobApplication FollowUp(
        int id,
        string company,
        ApplicationStatus status,
        DateOnly due,
        string? nextAction) =>
        new()
        {
            Id = id,
            UserId = "user-a",
            Company = company,
            RoleTitle = "Engineer",
            Status = status,
            NextActionDue = due,
            NextAction = nextAction
        };

    private static string Feed(params JobApplication[] apps)
    {
        var feed = InterviewPacketService.BuildCalendarFeed(apps, "https://calendar.example");
        Assert.StartsWith("BEGIN:VCALENDAR", feed, StringComparison.Ordinal);
        Assert.EndsWith("END:VCALENDAR", feed.TrimEnd('\r', '\n'), StringComparison.Ordinal);
        return feed;
    }

    private static int EventCount(string feed) => feed.Split("BEGIN:VEVENT").Length - 1;

    private static string SingleEvent(string feed)
    {
        var parts = feed.Split("BEGIN:VEVENT\r\n");
        Assert.Equal(2, parts.Length);
        return parts[1].Split("END:VEVENT")[0];
    }
}
