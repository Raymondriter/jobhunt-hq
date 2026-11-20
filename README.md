# JobHunt HQ

A job application tracker — and a hands-on Azure learning project. Track every
application, never miss a follow-up.

JobHunt HQ is a multi-user web app for managing a job search as a pipeline:
save postings, record applications, track each one through its stages, keep a
next action with a due date, and see the whole search on one dashboard.

**Live:** https://app-jobtracker-riter-h9gph8h2bgd4ddbm.westus2-01.azurewebsites.net/

## Stack

- **ASP.NET Core (.NET 10 LTS)** — MVC, Razor views, EF Core (from M2)
- **Azure App Service** — Linux, Free (F1) plan
- **Azure SQL Database** — serverless, free offer (from M2)
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

- **M1 — Hello Azure** *(in progress)*: Azure foundation is live — budget alert,
  resource group, F1 plan, and the app running at the URL above. Remaining:
  continuous deployment from GitHub.
- **M2 — The real app** *(next)*: ASP.NET Core Identity sign-in, EF Core model
  against Azure SQL, full CRUD for applications with the status pipeline
  (Saved → Applied → Screening → Interview → Offer, plus Rejected / Ghosted /
  Withdrawn), next action + due date per application, dashboard counts, and CSV
  import of real applications as seed data.
- **M3 — Cloud-native pieces**: resume uploads to Blob tied to applications,
  the daily reminder Function emailing a digest (Azure Communication Services
  Email), secrets moved to Key Vault behind managed identity, and an App
  Insights dashboard.

## Run it locally

Requires the .NET 10 SDK.

```bash
dotnet restore
dotnet run
```

Then open the URL printed in the console (default profiles are in
`Properties/launchSettings.json`).

## Deployment

Deployed to Azure App Service from GitHub Actions. The workflow and the
publish-profile secret are configured through the Azure portal's
**Deployment Center** for the `app-jobtracker-riter` web app — they are managed
there rather than hand-authored in this repo, so there is no `.github/workflows`
folder to edit here. Merges to `main` deploy automatically once Deployment
Center is connected.

## Project layout

```
Controllers/   MVC controllers (HomeController today; Applications in M2)
Models/        View models and (from M2) EF Core entities
Views/         Razor views — Shared/_Layout plus one folder per controller
wwwroot/       Static files (css/site.css)
Program.cs     Startup: services + middleware pipeline
```
