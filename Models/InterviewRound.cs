using System.ComponentModel.DataAnnotations;

namespace JobHuntHQ.Models;

/// <summary>
/// One scheduled or completed interview round for a <see cref="JobApplication"/>,
/// owned by the same user as the parent application.
/// </summary>
public class InterviewRound
{
    public int Id { get; set; }

    /// <summary>Owner (ASP.NET Core Identity user id). Set server-side, never bound from a form.</summary>
    [Required]
    public string UserId { get; set; } = string.Empty;

    public int JobApplicationId { get; set; }

    public JobApplication? JobApplication { get; set; }

    [Range(1, 20)]
    [Display(Name = "Round #")]
    public int RoundNumber { get; set; } = 1;

    [Required]
    [StringLength(100)]
    [Display(Name = "Round type")]
    public string RoundType { get; set; } = "Recruiter Screen";

    /// <summary>Scheduled date and time in the candidate's local wall-clock time.</summary>
    [Display(Name = "Scheduled date & time")]
    public DateTime? ScheduledLocal { get; set; }

    [StringLength(60)]
    [Display(Name = "Time zone")]
    public string? TimeZoneLabel { get; set; }

    [Range(10, 480)]
    [Display(Name = "Duration (minutes)")]
    public int DurationMinutes { get; set; } = 45;

    [StringLength(1000)]
    [Display(Name = "Meeting link / location")]
    public string? MeetingUrl { get; set; }

    [StringLength(500)]
    [Display(Name = "Interviewer(s) & titles")]
    public string? Interviewers { get; set; }

    [StringLength(4000)]
    [Display(Name = "Round focus & prep notes")]
    public string? PrepNotes { get; set; }

    [StringLength(4000)]
    [Display(Name = "Questions to ask them")]
    public string? QuestionsToAsk { get; set; }

    [StringLength(4000)]
    [Display(Name = "Live call scratchpad & post-interview debrief")]
    public string? DebriefNotes { get; set; }

    public bool Completed { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime UpdatedUtc { get; set; }

    public static readonly string[] CommonRoundTypes =
    {
        "Recruiter Screen",
        "Hiring Manager",
        "Technical / Coding",
        "System Design / Architecture",
        "Team / Behavioral Panel",
        "Take-Home Review",
        "Final / Executive"
    };
}
