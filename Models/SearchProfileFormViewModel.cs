using System.ComponentModel.DataAnnotations;

namespace JobHuntHQ.Models;

/// <summary>
/// The editable fields of a <see cref="SearchProfile"/>. UserId is deliberately
/// absent: the controller sets it from the signed-in user (no overposting).
/// </summary>
public class SearchProfileFormViewModel
{
    [StringLength(1000)]
    [Display(Name = "Target roles")]
    public string? TargetRoles { get; set; }

    [StringLength(200)]
    [Display(Name = "Remote preference")]
    public string? RemotePreference { get; set; }

    [Display(Name = "Pay floor (annual)")]
    [Range(0, int.MaxValue, ErrorMessage = "Pay floor can't be negative.")]
    public int? PayFloor { get; set; }

    [StringLength(2000)]
    [Display(Name = "Must-haves")]
    public string? MustHaves { get; set; }

    [StringLength(2000)]
    [Display(Name = "Deal-breakers")]
    public string? DealBreakers { get; set; }

    [StringLength(2000)]
    [Display(Name = "Skip companies")]
    public string? SkipCompanies { get; set; }

    [Display(Name = "Standing answers")]
    public string? StandingAnswers { get; set; }

    public static SearchProfileFormViewModel FromEntity(SearchProfile profile) => new()
    {
        TargetRoles = profile.TargetRoles,
        RemotePreference = profile.RemotePreference,
        PayFloor = profile.PayFloor,
        MustHaves = profile.MustHaves,
        DealBreakers = profile.DealBreakers,
        SkipCompanies = profile.SkipCompanies,
        StandingAnswers = profile.StandingAnswers
    };

    public void ApplyTo(SearchProfile profile)
    {
        profile.TargetRoles = TargetRoles;
        profile.RemotePreference = RemotePreference;
        profile.PayFloor = PayFloor;
        profile.MustHaves = MustHaves;
        profile.DealBreakers = DealBreakers;
        profile.SkipCompanies = SkipCompanies;
        profile.StandingAnswers = StandingAnswers;
    }
}
