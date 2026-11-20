using System.ComponentModel.DataAnnotations;

namespace JobHuntHQ.Models;

/// <summary>
/// The editable fields of a <see cref="ResumeVersion"/>. Like the application
/// form, UserId is absent on purpose — the controller owns it.
/// </summary>
public class ResumeFormViewModel
{
    public int Id { get; set; }

    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Notes { get; set; }

    public static ResumeFormViewModel FromEntity(ResumeVersion resume) => new()
    {
        Id = resume.Id,
        Name = resume.Name,
        Notes = resume.Notes
    };

    public void ApplyTo(ResumeVersion resume)
    {
        resume.Name = Name;
        resume.Notes = Notes;
    }
}
