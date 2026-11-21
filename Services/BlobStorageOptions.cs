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
}
