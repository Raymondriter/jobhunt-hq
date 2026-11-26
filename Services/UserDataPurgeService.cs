using JobHuntHQ.Data;
using Microsoft.EntityFrameworkCore;

namespace JobHuntHQ.Services;

/// <summary>
/// Permanently deletes everything an account owns: uploaded resume blobs plus
/// every UserId-keyed row (applications, interview rounds, resume versions,
/// STAR stories, API tokens, and the search profile). The app tables key on
/// UserId with no foreign keys to the Identity user, so deleting the Identity
/// account alone would orphan all of this — every account-closure path
/// (Profile "delete all my data" and the Identity delete-account page) must
/// run this purge. Single implementation so the two paths can never drift.
/// </summary>
public class UserDataPurgeService
{
    private readonly ApplicationDbContext _context;
    private readonly IResumeStorageService _storage;

    public UserDataPurgeService(ApplicationDbContext context, IResumeStorageService storage)
    {
        _context = context;
        _storage = storage;
    }

    public async Task PurgeUserDataAsync(string userId)
    {
        var resumes = await _context.ResumeVersions
            .Where(r => r.UserId == userId)
            .ToListAsync();
        foreach (var r in resumes)
        {
            if (!string.IsNullOrWhiteSpace(r.BlobName))
            {
                await _storage.DeleteAsync(r.BlobName);
            }
        }

        var rounds = await _context.InterviewRounds.Where(r => r.UserId == userId).ToListAsync();
        var apps = await _context.JobApplications.Where(a => a.UserId == userId).ToListAsync();
        var stories = await _context.StoryBankItems.Where(s => s.UserId == userId).ToListAsync();
        var tokens = await _context.ApiTokens.Where(t => t.UserId == userId).ToListAsync();
        var profile = await _context.SearchProfiles.FirstOrDefaultAsync(p => p.UserId == userId);

        _context.InterviewRounds.RemoveRange(rounds);
        _context.JobApplications.RemoveRange(apps);
        _context.ResumeVersions.RemoveRange(resumes);
        _context.StoryBankItems.RemoveRange(stories);
        _context.ApiTokens.RemoveRange(tokens);
        if (profile is not null)
        {
            _context.SearchProfiles.Remove(profile);
        }

        await _context.SaveChangesAsync();
    }
}
