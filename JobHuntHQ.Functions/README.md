# JobHuntHQ.Functions

The serverless half of JobHunt HQ (M3): a single timer-triggered Azure
Function that emails every user their due follow-ups once a day.

## What it does

**DailyDigest** — runs daily at **12:00 UTC** (≈ 7:00 AM EST / 8:00 AM EDT,
NCRONTAB `0 0 12 * * *`). It queries Azure SQL for open applications whose
next action is due within the next 7 days or already overdue (closed
statuses — Rejected, Ghosted, Withdrawn — are excluded), groups them by
user, and sends each user one digest email via **Azure Communication
Services Email**:

- Subject: `JobHunt HQ — N follow-up(s) due this week`
- Plain-text and simple HTML bodies listing company, role, next action, and
  due date, with overdue items flagged **OVERDUE**.
- If nothing is due, nothing is sent. One user's send failure is logged and
  does not stop the others.

Data access is raw `Microsoft.Data.SqlClient` with a single fixed,
parameterless query — this is a read-only leaf service, so there is no EF
Core here by design.

## Passwordless, like the rest of the app

There are no passwords or keys in code or config:

- **SQL** — the connection string specifies Active Directory Managed
  Identity authentication; SqlClient acquires the token itself using the
  Function app's managed identity (`AZURE_CLIENT_ID` selects the
  user-assigned identity). The database contains a matching user created
  `FROM EXTERNAL PROVIDER`.
- **Email** — the ACS connection string is the one secret-shaped value, and
  in Azure it is supplied as a **Key Vault reference** app setting, never a
  literal value in the repo or in plain app configuration.

## App settings (Azure) / local.settings.json (local)

| Setting | Purpose |
|---|---|
| `DefaultConnection` | Azure SQL connection string (managed-identity auth, no password). |
| `AcsEmailConnectionString` | Azure Communication Services connection string. In Azure this is a Key Vault reference. |
| `DigestFromAddress` | The verified ACS sender address the digest is sent from. |
| `AZURE_CLIENT_ID` | Client id of the user-assigned managed identity (SQL token acquisition). |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | App Insights telemetry for the worker process. |
| `AzureWebJobsStorage` | Standard Functions host storage connection. |

The committed `local.settings.json` contains placeholders only — real
values live exclusively in Azure app settings.

## Deployment

This project deploys to its **own Function app**, separately from the web
app. Its GitHub Actions workflow is created through that Function app's
**Deployment Center** in the Azure portal (same pattern as the web app),
building only this folder. The web app's workflow builds only the web
project at the repo root, so the two never interfere.
