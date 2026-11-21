using System.ComponentModel.DataAnnotations;

namespace JobHuntHQ.Models;

/// <summary>
/// A named resume version, optionally with its PDF file stored in Azure Blob
/// Storage (M3). The file fields below stay null until a file is uploaded;
/// the blob itself lives in a private container and is only ever served
/// through this app, never via a public link.
/// </summary>
public class ResumeVersion
{
    public int Id { get; set; }

    /// <summary>Owner (ASP.NET Core Identity user id). Set server-side, never bound from a form.</summary>
    [Required]
    public string UserId { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Notes { get; set; }

    public DateTime CreatedUtc { get; set; }

    /// <summary>Original file name of the uploaded PDF (e.g. "resume-2026.pdf"), for display and download.</summary>
    [StringLength(260)]
    public string? FileName { get; set; }

    /// <summary>Blob name inside the resumes container: "{userId}/{resumeVersionId}/{guid}.pdf".</summary>
    [StringLength(400)]
    public string? BlobName { get; set; }

    [StringLength(100)]
    public string? ContentType { get; set; }

    public long? SizeBytes { get; set; }
}
