using JobHuntHQ.Services;

namespace JobHuntHQ.Tests;

public class LocalResumeStorageServiceTests
{
    [Fact]
    public async Task Upload_and_delete_stay_inside_the_configured_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "jhq-resumes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var storage = new LocalResumeStorageService(root);
            await using var content = new MemoryStream("pdf"u8.ToArray());
            await storage.UploadAsync("user-a/resume.pdf", content, "application/pdf");

            var stored = Path.Combine(root, "user-a", "resume.pdf");
            Assert.True(File.Exists(stored));

            await using var downloaded = await storage.DownloadAsync("user-a/resume.pdf");
            Assert.NotNull(downloaded);

            await storage.DeleteAsync("user-a/resume.pdf");
            Assert.False(File.Exists(stored));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("../outside.pdf")]
    [InlineData("user/../../outside.pdf")]
    public async Task Delete_and_upload_reject_paths_that_leave_the_folder(string blobName)
    {
        var root = Path.Combine(Path.GetTempPath(), "jhq-resumes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var outside = Path.Combine(Directory.GetParent(root)!.FullName, "outside.pdf");
        try
        {
            var storage = new LocalResumeStorageService(root);
            await using var content = new MemoryStream("pdf"u8.ToArray());

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                storage.UploadAsync(blobName, content, "application/pdf"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => storage.DeleteAsync(blobName));
            Assert.False(File.Exists(outside));
            Assert.Empty(Directory.GetFiles(root, "*", SearchOption.AllDirectories));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
            if (File.Exists(outside))
            {
                File.Delete(outside);
            }
        }
    }
}
