using System.Text;
using JobHuntHQ.Models;
using UglyToad.PdfPig;

namespace JobHuntHQ.Services;

/// <summary>Outcome of a resume-text extraction attempt. <see cref="Text"/> is null whenever extraction didn't happen or failed — <see cref="Note"/> then explains why, for the packet.</summary>
public sealed record ResumeTextResult(string? Text, bool Truncated, string? Note);

/// <summary>
/// Extracts plain text from a resume version's uploaded PDF (M4 phase 2) so
/// the AI packet can carry the resume's full text inline — an AI assistant
/// can't reliably fetch a Blob URL, but it can read text in the packet.
/// Extraction is best-effort by design: any failure (no file, unreachable
/// storage, unparseable or image-only PDF) falls back to metadata plus a
/// note telling the user to attach the file themselves. Never throws.
/// </summary>
public class ResumeTextService
{
    /// <summary>Packet size guard: resume text is capped at this many characters.</summary>
    public const int MaxChars = 30_000;

    private readonly IResumeStorageService _storage;

    public ResumeTextService(IResumeStorageService storage)
    {
        _storage = storage;
    }

    public async Task<ResumeTextResult> ExtractAsync(ResumeVersion resume, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(resume.BlobName))
        {
            return new ResumeTextResult(null, false,
                "No PDF file is uploaded for this resume version in JobHunt HQ, so the packet carries metadata " +
                "only — attach the actual PDF file when you hand the packet to your AI.");
        }

        try
        {
            await using var stream = await _storage.DownloadAsync(resume.BlobName, cancellationToken);
            if (stream is null)
            {
                return new ResumeTextResult(null, false,
                    "The uploaded PDF could not be retrieved from storage, so the packet carries metadata " +
                    "only — attach the actual PDF file when you hand the packet to your AI.");
            }

            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            buffer.Position = 0;

            var sb = new StringBuilder();
            using (var document = PdfDocument.Open(buffer))
            {
                foreach (var page in document.GetPages())
                {
                    sb.AppendLine(page.Text);
                    if (sb.Length > MaxChars)
                    {
                        break;
                    }
                }
            }

            var text = sb.ToString().Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return new ResumeTextResult(null, false,
                    "The uploaded PDF contains no extractable text (it may be scanned images), so the packet " +
                    "carries metadata only — attach the actual PDF file when you hand the packet to your AI.");
            }

            var truncated = text.Length > MaxChars;
            if (truncated)
            {
                text = text[..MaxChars];
            }
            return new ResumeTextResult(text, truncated, null);
        }
        catch (Exception)
        {
            // Deliberately swallow: a broken PDF must never break the packet.
            return new ResumeTextResult(null, false,
                "The uploaded PDF could not be read, so the packet carries metadata only — attach the actual " +
                "PDF file when you hand the packet to your AI.");
        }
    }
}
