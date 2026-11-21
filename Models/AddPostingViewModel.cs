using System.ComponentModel.DataAnnotations;

namespace JobHuntHQ.Models;

/// <summary>
/// The "Add posting" capture form (M4): paste a posting, and JobHunt HQ saves
/// it as a Saved application and immediately runs the fit check against the
/// user's search profile.
/// </summary>
public class AddPostingViewModel
{
    [Required]
    [StringLength(200)]
    public string Company { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    [Display(Name = "Role title")]
    public string JobTitle { get; set; } = string.Empty;

    [StringLength(1000)]
    [Display(Name = "Posting URL")]
    public string? PostingUrl { get; set; }

    [Display(Name = "Posting text (paste the full posting)")]
    public string? PostingText { get; set; }

    [Display(Name = "Pay min (annual, if listed)")]
    [Range(0, int.MaxValue, ErrorMessage = "Pay can't be negative.")]
    public int? PayMin { get; set; }

    [Display(Name = "Pay max (annual, if listed)")]
    [Range(0, int.MaxValue, ErrorMessage = "Pay can't be negative.")]
    public int? PayMax { get; set; }

    /// <summary>Outcome note from a "Fetch from URL" round-trip (success or friendly failure). Display only — never saved.</summary>
    public string? FetchNote { get; set; }
}
