using System.Net;
using Azure;
using Azure.Communication.Email;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace JobHuntHQ.Functions;

/// <summary>
/// Daily follow-up digest (M3). Once a day, finds every open application whose
/// next action is due within the next 7 days (or already overdue) and emails
/// each user their own list via Azure Communication Services Email.
///
/// Deliberately built on raw Microsoft.Data.SqlClient rather than EF Core:
/// this is a small read-only leaf service, and the query is a single fixed
/// statement with no user input. The connection string carries
/// "Active Directory Managed Identity" authentication, so SqlClient acquires
/// the token itself — there is no credential code here and no password.
/// </summary>
public class DailyDigestFunction
{
    // Status values 5, 6, 7 are the closed states (Rejected, Ghosted,
    // Withdrawn) in the web app's ApplicationStatus enum.
    private const string DueFollowUpsSql = """
        SELECT u.Email AS UserEmail, a.Company, a.RoleTitle, a.NextAction, a.NextActionDue
        FROM dbo.JobApplications a
        INNER JOIN dbo.AspNetUsers u ON u.Id = a.UserId
        WHERE a.NextActionDue IS NOT NULL
          AND a.NextActionDue <= DATEADD(day, 7, CAST(GETUTCDATE() AS date))
          AND a.Status NOT IN (5, 6, 7)
        ORDER BY u.Email, a.NextActionDue, a.Company;
        """;

    private readonly ILogger _logger;

    public DailyDigestFunction(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<DailyDigestFunction>();
    }

    private sealed record DigestItem(string Company, string RoleTitle, string? NextAction, DateOnly Due);

    // NCRONTAB "0 0 12 * * *" = 12:00:00 UTC every day — that's 7:00 AM EST /
    // 8:00 AM EDT, so the digest lands with the morning coffee year-round.
    [Function("DailyDigest")]
    public async Task Run([TimerTrigger("0 0 12 * * *")] TimerInfo timerInfo)
    {
        _logger.LogInformation("DailyDigest started at {TimeUtc} (next: {NextUtc}).",
            DateTime.UtcNow, timerInfo.ScheduleStatus?.Next);

        var connectionString = Environment.GetEnvironmentVariable("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            _logger.LogError("DefaultConnection app setting is not configured; cannot build the digest.");
            return;
        }

        var byUser = await LoadDueFollowUpsAsync(connectionString);
        if (byUser.Count == 0)
        {
            _logger.LogInformation("No follow-ups due — no digest emails to send.");
            return;
        }

        var acsConnectionString = Environment.GetEnvironmentVariable("AcsEmailConnectionString");
        var fromAddress = Environment.GetEnvironmentVariable("DigestFromAddress");
        if (string.IsNullOrWhiteSpace(acsConnectionString) || string.IsNullOrWhiteSpace(fromAddress))
        {
            _logger.LogError("AcsEmailConnectionString and/or DigestFromAddress app settings are not configured; {UserCount} digest(s) cannot be sent.", byUser.Count);
            return;
        }

        var emailClient = new EmailClient(acsConnectionString);
        var todayUtc = DateOnly.FromDateTime(DateTime.UtcNow);

        foreach (var (userEmail, items) in byUser)
        {
            // One user's failure must not stop the remaining digests.
            try
            {
                await SendDigestAsync(emailClient, fromAddress, userEmail, items, todayUtc);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send the digest to {Email} ({Count} item(s) skipped).", userEmail, items.Count);
            }
        }
    }

    private async Task<Dictionary<string, List<DigestItem>>> LoadDueFollowUpsAsync(string connectionString)
    {
        var byUser = new Dictionary<string, List<DigestItem>>(StringComparer.OrdinalIgnoreCase);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new SqlCommand(DueFollowUpsSql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var userEmail = reader.GetString(0);
            if (string.IsNullOrWhiteSpace(userEmail))
            {
                _logger.LogWarning("Skipping a due follow-up with no user email (company: {Company}).",
                    reader.IsDBNull(1) ? "?" : reader.GetString(1));
                continue;
            }

            var item = new DigestItem(
                Company: reader.GetString(1),
                RoleTitle: reader.GetString(2),
                NextAction: reader.IsDBNull(3) ? null : reader.GetString(3),
                Due: DateOnly.FromDateTime(reader.GetDateTime(4)));

            if (!byUser.TryGetValue(userEmail, out var list))
            {
                list = new List<DigestItem>();
                byUser[userEmail] = list;
            }
            list.Add(item);
        }

        return byUser;
    }

    private async Task SendDigestAsync(EmailClient emailClient, string fromAddress, string userEmail,
        List<DigestItem> items, DateOnly todayUtc)
    {
        var subject = $"JobHunt HQ — {items.Count} follow-up{(items.Count == 1 ? "" : "s")} due this week";

        var plainLines = new List<string>
        {
            $"You have {items.Count} follow-up{(items.Count == 1 ? "" : "s")} due in the next 7 days:",
            ""
        };
        var htmlItems = new List<string>();

        foreach (var item in items)
        {
            var overdue = item.Due < todayUtc;
            var dueText = item.Due.ToString("yyyy-MM-dd") + (overdue ? " (OVERDUE)" : "");
            var actionText = string.IsNullOrWhiteSpace(item.NextAction) ? "—" : item.NextAction;

            plainLines.Add($"- {item.Company} — {item.RoleTitle}");
            plainLines.Add($"  Next action: {actionText}");
            plainLines.Add($"  Due: {dueText}");
            plainLines.Add("");

            htmlItems.Add(
                $"<li><strong>{WebUtility.HtmlEncode(item.Company)}</strong> — {WebUtility.HtmlEncode(item.RoleTitle)}<br/>" +
                $"Next action: {WebUtility.HtmlEncode(actionText)}<br/>" +
                $"Due: {item.Due:yyyy-MM-dd}{(overdue ? " <strong>(OVERDUE)</strong>" : "")}</li>");
        }

        plainLines.Add("— JobHunt HQ");

        var html = "<html><body>"
            + $"<p>You have <strong>{items.Count}</strong> follow-up{(items.Count == 1 ? "" : "s")} due in the next 7 days:</p>"
            + "<ul>" + string.Join("", htmlItems) + "</ul>"
            + "<p>— JobHunt HQ</p>"
            + "</body></html>";

        var content = new EmailContent(subject)
        {
            PlainText = string.Join(Environment.NewLine, plainLines),
            Html = html
        };
        var message = new EmailMessage(fromAddress, userEmail, content);

        EmailSendOperation operation = await emailClient.SendAsync(WaitUntil.Completed, message);
        _logger.LogInformation("Digest sent to {Email} ({Count} item(s)): message id {MessageId}, status {Status}.",
            userEmail, items.Count, operation.Id, operation.Value.Status);
    }
}
