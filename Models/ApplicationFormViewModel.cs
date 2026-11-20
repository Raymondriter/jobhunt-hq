using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace JobHuntHQ.Models;

/// <summary>
/// The editable fields of a <see cref="JobApplication"/>. UserId is deliberately
/// absent: the controller sets it from the signed-in user, so a form post can
/// never assign an application to somebody else (no overposting).
/// </summary>
public class ApplicationFormViewModel
{
    public int Id { get; set; }

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
    public DateOnly DateSaved { get; set; } = DateOnly.FromDateTime(DateTime.Today);

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

    [StringLength(4000)]
    public string? Notes { get; set; }

    /// <summary>The current user's resume versions, for the "Resume used" dropdown.</summary>
    public List<SelectListItem> ResumeOptions { get; set; } = new();

    public static ApplicationFormViewModel FromEntity(JobApplication app) => new()
    {
        Id = app.Id,
        Company = app.Company,
        RoleTitle = app.RoleTitle,
        Url = app.Url,
        Location = app.Location,
        Status = app.Status,
        DateSaved = app.DateSaved,
        DateApplied = app.DateApplied,
        NextAction = app.NextAction,
        NextActionDue = app.NextActionDue,
        SalaryMin = app.SalaryMin,
        SalaryMax = app.SalaryMax,
        ResumeVersionId = app.ResumeVersionId,
        Notes = app.Notes
    };

    public void ApplyTo(JobApplication app)
    {
        app.Company = Company;
        app.RoleTitle = RoleTitle;
        app.Url = Url;
        app.Location = Location;
        app.Status = Status;
        app.DateSaved = DateSaved;
        app.DateApplied = DateApplied;
        app.NextAction = NextAction;
        app.NextActionDue = NextActionDue;
        app.SalaryMin = SalaryMin;
        app.SalaryMax = SalaryMax;
        app.ResumeVersionId = ResumeVersionId;
        app.Notes = Notes;
    }
}
