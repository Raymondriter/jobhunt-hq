using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;

namespace JobHuntHQ.Services;

/// <summary>
/// Thin wrapper over Azure Blob Storage for resume PDF files, so controllers
/// never touch the Blob SDK directly. All access goes through the app's
/// managed identity (see Program.cs) — no storage keys.
/// </summary>
public interface IResumeStorageService
{
    /// <summary>Uploads (or overwrites) the blob with the given name.</summary>
    Task UploadAsync(string blobName, Stream content, string contentType, CancellationToken cancellationToken = default);

    /// <summary>Opens a read stream for the blob, or null when it does not exist.</summary>
    Task<Stream?> DownloadAsync(string blobName, CancellationToken cancellationToken = default);

    /// <summary>Deletes the blob when present; a missing blob is not an error.</summary>
    Task DeleteAsync(string blobName, CancellationToken cancellationToken = default);
}

public class ResumeStorageService : IResumeStorageService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly BlobStorageOptions _options;

    public ResumeStorageService(BlobServiceClient blobServiceClient, IOptions<BlobStorageOptions> options)
    {
        _blobServiceClient = blobServiceClient;
        _options = options.Value;
    }

    private BlobContainerClient Container =>
        _blobServiceClient.GetBlobContainerClient(_options.ContainerName);

    public async Task UploadAsync(string blobName, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        var container = Container;
        // Private container (no public access) — files are only ever served
        // back through this app after an ownership check.
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);

        var blob = container.GetBlobClient(blobName);
        await blob.UploadAsync(content, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
        }, cancellationToken);
    }

    public async Task<Stream?> DownloadAsync(string blobName, CancellationToken cancellationToken = default)
    {
        var blob = Container.GetBlobClient(blobName);
        if (!await blob.ExistsAsync(cancellationToken))
        {
            return null;
        }

        var download = await blob.DownloadStreamingAsync(cancellationToken: cancellationToken);
        return download.Value.Content;
    }

    public async Task DeleteAsync(string blobName, CancellationToken cancellationToken = default)
    {
        await Container.GetBlobClient(blobName)
            .DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: cancellationToken);
    }
}

/// <summary>
/// Local filesystem implementation of <see cref="IResumeStorageService"/> used only in
/// Development when <see cref="BlobStorageOptions.LocalFolder"/> is configured.
/// </summary>
public class LocalResumeStorageService : IResumeStorageService
{
    private readonly string _rootFolder;

    public LocalResumeStorageService(string rootFolder)
    {
        _rootFolder = Path.GetFullPath(rootFolder);
    }

    private string ResolveSafePath(string blobName)
    {
        var normalized = blobName.Replace('\\', '/').TrimStart('/');
        var fullPath = Path.GetFullPath(Path.Combine(_rootFolder, normalized));
        if (!fullPath.StartsWith(_rootFolder, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Invalid blob path.");
        }
        return fullPath;
    }

    public async Task UploadAsync(string blobName, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        var path = ResolveSafePath(blobName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(fileStream, cancellationToken);
    }

    public Task<Stream?> DownloadAsync(string blobName, CancellationToken cancellationToken = default)
    {
        var path = ResolveSafePath(blobName);
        if (!File.Exists(path))
        {
            return Task.FromResult<Stream?>(null);
        }
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult<Stream?>(stream);
    }

    public Task DeleteAsync(string blobName, CancellationToken cancellationToken = default)
    {
        var path = ResolveSafePath(blobName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        return Task.CompletedTask;
    }
}
