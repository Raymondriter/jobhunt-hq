namespace JobHuntHQ.Models;

/// <summary>
/// The "Download my data" export (phase 3): everything JobHunt HQ holds for
/// one user, as a single JSON document — their search profile, every
/// application with all its fields, and resume version metadata.
///
/// Deliberately NOT included: the user's id, resume blob names (internal
/// storage paths), and the resume PDF bytes themselves — PDFs are downloaded
/// individually from the Resumes page. Property names serialize as written
/// (PascalCase), matching the AI packet's JSON shape.
/// </summary>
public sealed class UserDataExport
{
    public string ExportVersion { get; init; } = "2";

    public DateTime ExportedAtUtc { get; init; }

    /// <summary>The account's sign-in email, so the file says whose data it is.</summary>
    public string AccountEmail { get; init; } = string.Empty;

    public SearchProfileExport? SearchProfile { get; init; }

    public List<ApplicationExport> Applications { get; init; } = new();

    public List<ResumeVersionExport> ResumeVersions { get; init; } = new();

    public List<StoryBankExport> StoryBank { get; init; } = new();
}

public sealed class SearchProfileExport
{
    public int Id { get; init; }
    public string? TargetRoles { get; init; }
    public string? RemotePreference { get; init; }
    public int? PayFloor { get; init; }
    public string? MustHaves { get; init; }
    public string? DealBreakers { get; init; }
    public string? SkipCompanies { get; init; }
    public string? StandingAnswers { get; init; }
    public DateTime CreatedUtc { get; init; }
    public DateTime UpdatedUtc { get; init; }

    public static SearchProfileExport FromEntity(SearchProfile profile) => new()
    {
        Id = profile.Id,
        TargetRoles = profile.TargetRoles,
        RemotePreference = profile.RemotePreference,
        PayFloor = profile.PayFloor,
        MustHaves = profile.MustHaves,
        DealBreakers = profile.DealBreakers,
        SkipCompanies = profile.SkipCompanies,
        StandingAnswers = profile.StandingAnswers,
        CreatedUtc = profile.CreatedUtc,
        UpdatedUtc = profile.UpdatedUtc
    };
}

public sealed class ApplicationExport
{
    public int Id { get; init; }
    public string Company { get; init; } = string.Empty;
    public string RoleTitle { get; init; } = string.Empty;
    public string? Url { get; init; }
    public string? Location { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateOnly DateSaved { get; init; }
    public DateOnly? DateApplied { get; init; }
    public string? NextAction { get; init; }
    public DateOnly? NextActionDue { get; init; }
    public int? SalaryMin { get; init; }
    public int? SalaryMax { get; init; }
    public int? ResumeVersionId { get; init; }
    public string? Notes { get; init; }
    public string? PostingUrl { get; init; }
    public string? PostingText { get; init; }
    public int? PayMin { get; init; }
    public int? PayMax { get; init; }
    public string? FitVerdict { get; init; }
    public string? FitReasons { get; init; }
    public string? ConfirmationReference { get; init; }
    public string? BlockingQuestion { get; init; }
    public string? ExternalAttemptId { get; init; }
    public string? SourceAgent { get; init; }
    public string? ElevatorPitch { get; init; }
    public string? CompanyResearch { get; init; }
    public List<InterviewRoundExport> InterviewRounds { get; init; } = new();
    public DateTime CreatedUtc { get; init; }
    public DateTime UpdatedUtc { get; init; }

    public static ApplicationExport FromEntity(JobApplication application) => new()
    {
        Id = application.Id,
        Company = application.Company,
        RoleTitle = application.RoleTitle,
        Url = application.Url,
        Location = application.Location,
        Status = application.Status.ToString(),
        DateSaved = application.DateSaved,
        DateApplied = application.DateApplied,
        NextAction = application.NextAction,
        NextActionDue = application.NextActionDue,
        SalaryMin = application.SalaryMin,
        SalaryMax = application.SalaryMax,
        ResumeVersionId = application.ResumeVersionId,
        Notes = application.Notes,
        PostingUrl = application.PostingUrl,
        PostingText = application.PostingText,
        PayMin = application.PayMin,
        PayMax = application.PayMax,
        FitVerdict = application.FitVerdict,
        FitReasons = application.FitReasons,
        ConfirmationReference = application.ConfirmationReference,
        BlockingQuestion = application.BlockingQuestion,
        ExternalAttemptId = application.ExternalAttemptId,
        SourceAgent = application.SourceAgent,
        ElevatorPitch = application.ElevatorPitch,
        CompanyResearch = application.CompanyResearch,
        InterviewRounds = application.InterviewRounds
            .OrderBy(r => r.RoundNumber)
            .ThenBy(r => r.Id)
            .Select(InterviewRoundExport.FromEntity)
            .ToList(),
        CreatedUtc = application.CreatedUtc,
        UpdatedUtc = application.UpdatedUtc
    };
}

public sealed class InterviewRoundExport
{
    public int Id { get; init; }
    public int RoundNumber { get; init; }
    public string RoundType { get; init; } = string.Empty;
    public DateTime? ScheduledLocal { get; init; }
    public string? TimeZoneLabel { get; init; }
    public int DurationMinutes { get; init; }
    public string? MeetingUrl { get; init; }
    public string? Interviewers { get; init; }
    public string? PrepNotes { get; init; }
    public string? QuestionsToAsk { get; init; }
    public string? DebriefNotes { get; init; }
    public bool Completed { get; init; }
    public DateTime CreatedUtc { get; init; }
    public DateTime UpdatedUtc { get; init; }

    public static InterviewRoundExport FromEntity(InterviewRound round) => new()
    {
        Id = round.Id,
        RoundNumber = round.RoundNumber,
        RoundType = round.RoundType,
        ScheduledLocal = round.ScheduledLocal,
        TimeZoneLabel = round.TimeZoneLabel,
        DurationMinutes = round.DurationMinutes,
        MeetingUrl = round.MeetingUrl,
        Interviewers = round.Interviewers,
        PrepNotes = round.PrepNotes,
        QuestionsToAsk = round.QuestionsToAsk,
        DebriefNotes = round.DebriefNotes,
        Completed = round.Completed,
        CreatedUtc = round.CreatedUtc,
        UpdatedUtc = round.UpdatedUtc
    };
}

public sealed class StoryBankExport
{
    public int Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Tags { get; init; }
    public string? Situation { get; init; }
    public string? Task { get; init; }
    public string? Action { get; init; }
    public string? Result { get; init; }
    public DateTime CreatedUtc { get; init; }
    public DateTime UpdatedUtc { get; init; }

    public static StoryBankExport FromEntity(StoryBankItem item) => new()
    {
        Id = item.Id,
        Title = item.Title,
        Tags = item.Tags,
        Situation = item.Situation,
        Task = item.Task,
        Action = item.Action,
        Result = item.Result,
        CreatedUtc = item.CreatedUtc,
        UpdatedUtc = item.UpdatedUtc
    };
}

public sealed class ResumeVersionExport
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Notes { get; init; }
    public DateTime CreatedUtc { get; init; }

    /// <summary>Original file name of the uploaded PDF, if one was uploaded.</summary>
    public string? FileName { get; init; }

    public string? ContentType { get; init; }

    public long? SizeBytes { get; init; }

    public static ResumeVersionExport FromEntity(ResumeVersion resume) => new()
    {
        Id = resume.Id,
        Name = resume.Name,
        Notes = resume.Notes,
        CreatedUtc = resume.CreatedUtc,
        FileName = resume.FileName,
        ContentType = resume.ContentType,
        SizeBytes = resume.SizeBytes
    };
}
