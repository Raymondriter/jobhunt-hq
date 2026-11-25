using System.ComponentModel.DataAnnotations;

namespace JobHuntHQ.Models;

/// <summary>
/// One real STAR story in a user's reusable interview story bank.
/// Stored once per user and surfaced across every interview workspace and
/// Interview AI Packet so preparation stays 100% grounded in real experience.
/// </summary>
public class StoryBankItem
{
    public int Id { get; set; }

    /// <summary>Owner (ASP.NET Core Identity user id). Set server-side, never bound from a form.</summary>
    [Required]
    public string UserId { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    [Display(Name = "Story headline")]
    public string Title { get; set; } = string.Empty;

    /// <summary>Comma-separated skill or competency tags (e.g. "C#, Azure, Managed Identity, Ownership").</summary>
    [StringLength(500)]
    [Display(Name = "Skills & themes (comma-separated)")]
    public string? Tags { get; set; }

    [StringLength(2000)]
    [Display(Name = "Situation (context & stakes)")]
    public string? Situation { get; set; }

    [StringLength(2000)]
    [Display(Name = "Task (what needed solving)")]
    public string? Task { get; set; }

    [StringLength(3000)]
    [Display(Name = "Action (what YOU specifically built or did)")]
    public string? Action { get; set; }

    [StringLength(2000)]
    [Display(Name = "Result (outcome, metrics, lessons learned)")]
    public string? Result { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime UpdatedUtc { get; set; }
}
