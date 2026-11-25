namespace JobHuntHQ.Services;

/// <summary>
/// Strongly-typed view of the "BlobStorage" configuration section.
/// In production, ServiceUri is set as an App Service app setting; the value
/// committed in appsettings.json is a placeholder.
/// </summary>
public class BlobStorageOptions
{
    public const string SectionName = "BlobStorage";

    public string ServiceUri { get; set; } = string.Empty;

    public string ContainerName { get; set; } = "resumes";

    /// <summary>
    /// Optional local directory path for Development environments without Azure Blob access.
    /// In production this is null/empty and Azure Blob Storage is used exclusively.
    /// </summary>
    public string? LocalFolder { get; set; }
}
