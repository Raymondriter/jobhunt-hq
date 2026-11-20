# Seed data

## applications-seed.csv

Generated **November 20, 2026** from Raymond's applications tracker
(`~/workspace/goals/remote-net-posting-scan/files/applications-tracker.md`,
the living record maintained by the daily application-progress watch).

It contains all **37 submitted applications** in the tracker's tables and
progress log, in the JobHunt HQ CSV import format. To use it: sign in to
JobHunt HQ, open **Import**, and upload this file. (Importing twice creates
duplicates — there is no de-duping yet.)

### How rows were mapped

- **Company / Title** — from the tracker's Company and Role columns.
- **Status** — `Rejected` where the tracker records a rejection (9 rows),
  otherwise `Applied` (28 rows). The tracker's finer-grained states
  ("Confirmation received", "Under Consideration", …) all map to `Applied`;
  no row had progressed to Screening/Interview/Offer as of Nov 20, 2026.
- **DateApplied** — the tracker's submission date. On import it also becomes
  DateSaved (the tracker doesn't record a separate saved date).
- **SalaryMin / SalaryMax** — the posted pay band from the tracker, in whole
  dollars. Left blank where the posting's pay was unlisted.
- **Url, Location, NextAction, NextActionDue, ResumeName, Notes** — the
  tracker doesn't carry these per application, so they are blank. Nothing was
  invented to fill them.

### Mapping notes (judgment calls, all from tracker text)

- **PPLSI** is `Rejected`: its table cell still says "under review", but the
  tracker's progress log records the rejection on Nov 11, 2026 — the log wins.
- **Covr Technologies**' pay band ($105K–$115K) comes from the Nov 17, 2026
  progress-log entry (there is no table row for Covr).
- **IRIS Software Group, Aerostrat (×2), and Progressive Insurance** likewise
  come from Nov 17, 2026 progress-log entries rather than table rows.
- Requisition IDs were dropped from titles (e.g. Bonterra "R2026-0376",
  WellSky "JR5258"/"JR5407", OPENLANE "R-255090"). The two WellSky rows stay
  distinguishable by title ("Senior Software Engineer - Remote" from Nov 10,
  "Senior Software Engineer - .NET" from Nov 16).
- Company names were lightly normalized where the tracker carried a
  parenthetical: "Accruent (Fortive)" → Accruent, "Bonterra (NGP VAN)" →
  Bonterra, "CVS Health (MinuteClinic)" → CVS Health, "PPLSI (LegalShield)"
  → PPLSI, "Banyan Software (Point & Click Solutions)" → Banyan Software.
  "iA (Innovation Associates)" was kept as-is since "iA" alone is opaque.

### Rows that could not be mapped

None — all 37 submitted applications in the tracker mapped cleanly.
(Applications that were prepared but never submitted — e.g. Sharetec and
Valsoft, both blocked at submission — are not in the tracker as submitted and
are deliberately not in this file.)
