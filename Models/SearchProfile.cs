using System.ComponentModel.DataAnnotations;

namespace JobHuntHQ.Models;

/// <summary>
/// A user's own job-search criteria (M4): what they are looking for, what they
/// will not accept, and the reusable answers they paste into applications.
/// Exactly one profile per user (unique index on <see cref="UserId"/> in the
/// DbContext). Nothing here is shared between users, and no criteria are
/// baked in by the app — every field is the user's own words.
/// </summary>
public class SearchProfile
{
    public int Id { get; set; }

    /// <summary>Owner (ASP.NET Core Identity user id). Set server-side, never bound from a form.</summary>
    [Required]
    public string UserId { get; set; } = string.Empty;

    /// <summary>Roles the user is targeting, as a comma/newline-separated list of keywords.</summary>
    [StringLength(1000)]
    [Display(Name = "Target roles")]
    public string? TargetRoles { get; set; }

    /// <summary>Free-text remote preference, e.g. "Fully remote", "Hybrid — 2 days", "On-site OK".</summary>
    [StringLength(200)]
    [Display(Name = "Remote preference")]
    public string? RemotePreference { get; set; }

    /// <summary>Lowest annual pay the user will consider.</summary>
    [Display(Name = "Pay floor (annual)")]
    public int? PayFloor { get; set; }

    /// <summary>Things a posting must mention, comma/newline-separated keywords.</summary>
    [StringLength(2000)]
    [Display(Name = "Must-haves")]
    public string? MustHaves { get; set; }

    /// <summary>Keywords that disqualify a posting on sight, comma/newline-separated.</summary>
    [StringLength(2000)]
    [Display(Name = "Deal-breakers")]
    public string? DealBreakers { get; set; }

    /// <summary>Companies the user never wants to apply to, comma/newline-separated.</summary>
    [StringLength(2000)]
    [Display(Name = "Skip companies")]
    public string? SkipCompanies { get; set; }

    /// <summary>
    /// The user's reusable application answers (work authorization, salary
    /// expectations, standard question responses…). Long free text.
    /// </summary>
    [Display(Name = "Standing answers")]
    public string? StandingAnswers { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime UpdatedUtc { get; set; }
}
