# JobHunt HQ

A job application tracker — and a hands-on Azure learning project. Track every
application, never miss a follow-up.

JobHunt HQ is a multi-user web app for managing a job search as a pipeline:
save postings, record applications, track each one through its stages, keep a
next action with a due date, and see the whole search on one dashboard.

**Live:** https://app-jobtracker-riter-h9gph8h2bgd4ddbm.westus2-01.azurewebsites.net/

## Stack

- **ASP.NET Core (.NET 10 LTS)** — MVC, Razor views, ASP.NET Core Identity, EF Core
- **Azure App Service** — Linux, Free (F1) plan
- **Azure SQL Database** — serverless, free offer
- **Azure Blob Storage** — resume PDFs (M3)
- **Azure Functions** — timer-triggered daily follow-up digest (M3)
- **Azure Key Vault + managed identity** — secrets, no credentials in code (M3)
- **Application Insights** — requests, exceptions, dashboard (M3)
- **GitHub Actions** — build and deploy on merge to `main`

Everything lives in one Azure resource group (`rg-jobtracker-dev`, subscription
"JobHunt HQ") with a $5/month budget alert as a cost guardrail, targeting $0/month
on always-free tiers.

## Architecture

One deployable web app plus one small serverless piece:

| Piece | Role |
|---|---|
| App Service (F1) | Hosts the ASP.NET Core MVC app (UI + a few JSON endpoints). Sleeps when idle on the free tier. |
| Azure SQL (serverless) | Application data via EF Core. Auto-pauses when idle and at the free monthly limit. |
| Blob Storage | Resume PDFs in a private container; the app serves downloads itself — no public links. |
| Functions (Consumption) | Daily 8:00 AM ET timer: find follow-ups due in the next 48 hours and email a digest. |
| Key Vault | SQL connection string and email key, read via managed identity. |
| Application Insights | Telemetry with adaptive sampling and a daily ingestion cap. |

## Milestones

- **M1 — Hello Azure** *(complete)*: Azure foundation is live — budget alert,
  resource group, F1 plan, the app running at the URL above, and continuous
  deployment from GitHub via Deployment Center.
- **M2 — The real app** *(complete)*: ASP.NET Core Identity sign-in, EF Core
  model against Azure SQL, full CRUD for applications with the status pipeline
  (Saved → Applied → Screening → Interview → Offer, plus Rejected / Ghosted /
  Withdrawn), next action + due date per application, resume-version tracking,
  a per-user dashboard, and CSV import of real applications as seed data.
  Live since Nov 20, 2026: the database is passwordless (Entra-only auth; the
  app connects as its managed identity), the app applied its EF migrations at
  startup, and 37 real applications were imported from the tracker CSV.
- **M3 — Cloud-native pieces**: resume uploads to Blob tied to applications,
  the daily reminder Function emailing a digest (Azure Communication Services
  Email), secrets moved to Key Vault behind managed identity, and an App
  Insights dashboard.

## M2 features

- **Accounts** — register / log in with ASP.NET Core Identity. Registration is
  open; every user's data is strictly their own (all queries are scoped to the
  signed-in user, and another user's record returns 404, never a leak).
- **Applications** — full CRUD with a status filter, overdue / due-soon
  highlighting on next-action due dates, and an optional link to the resume
  version used.
- **Resumes** — resume versions tracked by name + notes. Metadata only for
  now; file upload to Blob Storage is M3.
- **Dashboard** — signed-in home page: counts per status, next actions due in
  the next 7 days, and the 5 most recently updated applications.
- **CSV import** — bulk-add applications from a tracker export (format below).
  A seed file built from a real tracker lives in `seed/`.

## CSV import format

The first row is a header row; column names match case-insensitively. Only
**Company** and **Title** are required.

| Column | Notes |
|---|---|
| Company | Required, max 200 chars. |
| Title | Required (`RoleTitle` also accepted). |
| Url | Posting link. |
| Location | Free text. |
| Status | One of Saved, Applied, Screening, Interview, Offer, Rejected, Ghosted, Withdrawn. Unrecognized values import as Saved with a warning. |
| DateApplied | `yyyy-MM-dd` or `M/d/yyyy`. Also used as the saved date. |
| NextAction | Free text. |
| NextActionDue | `yyyy-MM-dd` or `M/d/yyyy`. |
| SalaryMin / SalaryMax | Whole dollars (`$` and commas tolerated). |
| ResumeName | Matched to your resume versions by name; unmatched names are ignored with a warning. |
| Notes | Free text. |

The import result page reports how many rows imported, which rows were
skipped (line number + reason), and any warnings.

## Run it locally

Requires the .NET 10 SDK and a SQL Server the app can reach (LocalDB on
Windows works out of the box).

```bash
dotnet restore
dotnet run
```

The connection string is `ConnectionStrings:DefaultConnection` in
`appsettings.json` — the committed value is a LocalDB placeholder for local
dev. **In production the real Azure SQL connection string is set in the App
Service configuration (app settings) and is never committed to this repo.**
Pending migrations are applied automatically at startup.

Then open the URL printed in the console (default profiles are in
`Properties/launchSettings.json`). Register an account and you're in.

## Deployment

Deployed to Azure App Service from GitHub Actions. The workflow and the
publish-profile secret are configured through the Azure portal's
**Deployment Center** for the `app-jobtracker-riter` web app — they are managed
there rather than hand-authored in this repo, so there is no `.github/workflows`
folder to edit here. Merges to `main` deploy automatically once Deployment
Center is connected.

## Project layout

```
Controllers/   MVC controllers (Home, Applications, Resumes, Import)
Data/          ApplicationDbContext + design-time factory for the EF tools
Migrations/    EF Core migrations (InitialM2 creates Identity + app tables)
Models/        EF Core entities, the status enum, and view models
Services/      Small helpers (the hand-written CSV parser)
Views/         Razor views — Shared/_Layout plus one folder per controller
seed/          Seed CSV generated from a real applications tracker (+ notes)
wwwroot/       Static files (css/site.css)
Program.cs     Startup: services, Identity/EF wiring, migrate-at-startup, pipeline
```
