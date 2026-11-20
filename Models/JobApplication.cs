using System.ComponentModel.DataAnnotations;

namespace JobHuntHQ.Models;

/// <summary>
/// One tracked job application, owned by exactly one user.
/// </summary>
public class JobApplication
{
    public int Id { get; set; }

    /// <summary>Owner (ASP.NET Core Identity user id). Set server-side, never bound from a form.</summary>
    [Required]
    public string UserId { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string Company { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    [Display(Name = "Role title")]
    public string RoleTitle { get; set; } = string.Empty;

    [StringLength(1000)]
    [Display(Name = "Posting URL")]
    public string? Url { get; set; }

    [StringLength(200)]
    public string? Location { get; set; }

    public ApplicationStatus Status { get; set; } = ApplicationStatus.Saved;

    [Display(Name = "Date saved")]
    public DateOnly DateSaved { get; set; }

    [Display(Name = "Date applied")]
    public DateOnly? DateApplied { get; set; }

    [StringLength(500)]
    [Display(Name = "Next action")]
    public string? NextAction { get; set; }

    [Display(Name = "Next action due")]
    public DateOnly? NextActionDue { get; set; }

    [Display(Name = "Salary min")]
    public int? SalaryMin { get; set; }

    [Display(Name = "Salary max")]
    public int? SalaryMax { get; set; }

    [Display(Name = "Resume used")]
    public int? ResumeVersionId { get; set; }

    public ResumeVersion? ResumeVersion { get; set; }

    [StringLength(4000)]
    public string? Notes { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime UpdatedUtc { get; set; }
}
