using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;

namespace JobHuntHQ.Models;

/// <summary>
/// A user-owned personal API token (<c>docs/api-design.md</c>): generated once
/// with a <c>jhq_</c> prefix, shown once at creation, and stored exclusively
/// as a SHA-256 hash. Every API query is scoped to <see cref="UserId"/> and
/// restricted to <see cref="Scopes"/>. No scope ever permits submitting an
/// application.
/// </summary>
public class ApiToken
{
    public const string Prefix = "jhq_";
    public const string CalendarScope = "calendar:read";

    public static readonly (string Scope, string Description)[] AllScopes =
    {
        ("profile:read", "Read your search profile"),
        ("profile:write", "Update your search profile"),
        ("applications:read", "List/read applications and data export"),
        ("applications:write", "Create/edit applications and move board status"),
        ("postings:write", "Capture postings (paste or SSRF-guarded URL)"),
        ("fitcheck:run", "Run ad-hoc and saved fit checks"),
        ("packets:generate", "Generate Application & Interview AI packets"),
        (CalendarScope, "Read-only calendar feed of interviews & follow-ups")
    };

    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Short display prefix (e.g. "jhq_8f2a9c1b") so the user can identify which token to revoke.</summary>
    [Required]
    [StringLength(20)]
    public string TokenPrefix { get; set; } = string.Empty;

    /// <summary>Lowercase hex SHA-256 digest of the full raw token. Raw token is never persisted.</summary>
    [Required]
    [StringLength(64)]
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Comma-separated capability scopes.</summary>
    [Required]
    [StringLength(500)]
    public string Scopes { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; }

    public DateTime? LastUsedUtc { get; set; }

    public DateTime? RevokedUtc { get; set; }

    public bool IsRevoked => RevokedUtc.HasValue;

    public bool HasScope(string requiredScope) =>
        Scopes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(requiredScope, StringComparer.OrdinalIgnoreCase);

    public static (string RawToken, string DisplayPrefix, string Sha256Hex) Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var base64Url = Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        var raw = Prefix + base64Url;
        var displayPrefix = raw[..12];
        var hash = ComputeSha256Hex(raw);
        return (raw, displayPrefix, hash);
    }

    public static string ComputeSha256Hex(string rawToken)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexStringLower(hashBytes);
    }
}
