# JobHunt HQ — public API + MCP server design

**Status:** design only. No API code exists yet; this document is the plan a
future milestone builds from. It exists so the API's most important property
— what it *refuses* to do — is decided before any endpoint is written.

**Related:** `brand.md` (the never-submit principle, the palette/voice the
docs should use), `wwwroot/llms.txt` (the machine-readable product summary),
and the AI packet (`Services/AiPacketService.cs`, packetVersion 2), whose
JSON shape this API reuses rather than reinventing.

---

## 1. Principles

1. **Read + prepare only. There is no submit endpoint — ever.** The API can
   track, check, and prepare. It cannot apply, submit, send, or log into
   anything on the user's behalf. This is not a missing feature or a v2
   item: no endpoint, tool, scope, or token type may be added later that
   submits an application, fills a form on an employer's site, or stores a
   user's credentials for a job board or ATS. If a request needs one of
   those, the answer is the AI packet — a document the user hands to their
   own AI, which stops before submission.
2. **The user's own AI is the client we design for.** JobHunt HQ's users run
   their job search with a personal AI assistant. The API exists so that
   assistant can read the pipeline, add postings, run fit checks, and fetch
   packets — under the user's own token, with the user able to see and
   revoke everything.
3. **User-owned tokens, least privilege.** Access is via personal API tokens
   the user creates in the app, scoped per capability (§3). No shared
   secrets, no "admin" tokens, no access to anyone else's data — every query
   is scoped to the token's owner, exactly like the web UI's queries are
   scoped to the signed-in user today.
4. **Same services, same rules.** API endpoints call the same service layer
   as the web UI (`FitCheckService`, `AiPacketService`,
   `PostingFetchService`) so a fit check or packet is identical wherever it
   was requested, and the posting fetcher's SSRF guards apply to API calls
   unchanged.
5. **Boring and versioned.** REST + JSON, a `/v1` prefix, additive changes
   within a version, and errors in one documented shape.

## 2. Non-goals

- Submitting, auto-filling, or scheduling applications anywhere.
- Scraping job boards or aggregating postings — the API only ever handles
  postings the user supplies (pasted text, or a URL they provide, fetched
  under the existing SSRF-guarded fetcher).
- Team/shared accounts, multi-user workspaces, billing.
- Webhooks (revisit after public read; the digest email already covers
  "tell me what's due" for humans).

## 3. Authentication & authorization

**Personal API tokens** (phase 1+):

- Created by the signed-in user in the app (Settings → API tokens), shown
  **once** at creation, stored only as a hash (e.g. SHA-256 with a per-token
  salt or an HMAC with a server secret — decide at build time; the raw token
  is never stored or logged).
- Format: `jhq_` prefix + 32 random bytes, base64url — recognizable in logs
  and secret scanners without being valid anywhere else.
- Sent as `Authorization: Bearer <token>`. HTTPS only.
- Each token records: name, scopes, created, last used, and a revoked flag.
  Revocation is instant and self-serve; a user can hold several tokens
  (e.g. one per AI assistant) and kill one without touching the others.

**Scopes:**

| Scope | Allows |
|---|---|
| `profile:read` | GET the search profile |
| `profile:write` | Create/update the search profile |
| `applications:read` | List/get applications, get the data export |
| `applications:write` | Create an application, edit fields, change status (board moves) |
| `postings:write` | Add a posting (paste or URL), which creates a Saved application |
| `fitcheck:run` | Run ad-hoc and saved fit checks |
| `packets:generate` | Generate/download AI packets (Markdown or JSON) |

A token with only read scopes can never mutate anything, and no scope —
present or future — grants submission powers (Principle 1).

**Later (not phase 1):** OAuth 2.0 authorization-code flow for third-party
clients, if a real integration ever needs it. Tokens cover the
bring-your-own-AI case, which is the product's actual shape.

## 4. Rate limits & abuse controls

- **Per-token rate limit:** 300 requests / 5 minutes baseline; stricter
  buckets for expensive operations — packet generation 30 / 5 min, posting
  URL fetches 20 / 5 min (each fetch is real outbound work and inherits the
  web fetcher's SSRF validation, redirect re-checks, and size/time caps).
- **Per-user ceiling** across all their tokens, so splitting tokens doesn't
  multiply quota.
- `429` responses carry `Retry-After`; limits are stated in the docs, not
  discovered by users.
- **Audit:** token id, endpoint, timestamp, and result are logged (never
  token values, never posting text). The user can see a token's last-used
  time in the app.
- **Kill switches:** per-token revoke (user), per-user disable (operator),
  and a global API feature flag for incidents.
- Bulk-creation pattern detection on `postings:write` (e.g. hundreds of
  postings an hour) throttles first and flags for review — JobHunt HQ is a
  preparation tool for one person's search, not a firehose.

## 5. REST surface (v1)

Base: `https://<host>/api/v1`. All responses JSON. Dates: `yyyy-MM-dd` for
date-only fields, ISO 8601 UTC for timestamps — matching the packet JSON.

**Errors** — one shape everywhere:

```json
{ "error": { "code": "not_found", "message": "Application not found." } }
```

with `400` (bad input / unknown enum), `401` (missing/invalid token),
`403` (token lacks the scope), `404` (not found *or not yours* — another
user's id is indistinguishable from a missing one, as in the web UI),
`429` (rate limited).

### Profile

```
GET  /api/v1/profile            scope: profile:read
PUT  /api/v1/profile            scope: profile:write   (upsert — one per user)
```

Response (GET, and the profile object everywhere):

```json
{
  "targetRoles": ".NET developer, Software engineer",
  "remotePreference": "Fully remote",
  "payFloor": 95000,
  "mustHaves": "C#, Azure",
  "dealBreakers": "clearance required, on-call rotation",
  "skipCompanies": "Gainwell, Mindbody",
  "standingAnswers": "Authorized to work in the US: yes…",
  "createdAt": "2026-11-20T14:03:00Z",
  "updatedAt": "2026-11-21T09:15:00Z"
}
```

### Applications

```
GET    /api/v1/applications                 scope: applications:read
         ?status=Applied&dueBefore=2026-11-30&limit=50&cursor=…
GET    /api/v1/applications/{id}            scope: applications:read
POST   /api/v1/applications                 scope: applications:write
PATCH  /api/v1/applications/{id}            scope: applications:write
POST   /api/v1/applications/{id}/move       scope: applications:write
DELETE /api/v1/applications/{id}            scope: applications:write
```

Application object — all fields, mirroring the data export:

```json
{
  "id": 123,
  "company": "Example Co",
  "roleTitle": "Senior .NET Developer",
  "url": "https://…",
  "location": "Remote (US)",
  "status": "Applied",
  "dateSaved": "2026-11-18",
  "dateApplied": "2026-11-19",
  "nextAction": "Follow up with recruiter",
  "nextActionDue": "2026-11-26",
  "salaryMin": 120000,
  "salaryMax": 140000,
  "resumeVersionId": 7,
  "notes": "…",
  "postingUrl": "https://…",
  "postingText": "…",
  "payMin": 115000,
  "payMax": 145000,
  "fitVerdict": "Strong fit",
  "fitReasons": "…",
  "createdAt": "…",
  "updatedAt": "…"
}
```

`POST /applications/{id}/move` is the board drag-and-drop as an API call —
the same status change, the same ownership rule:

```json
// request
{ "status": "Interview" }
// response
{ "id": 123, "status": "Interview", "updatedAt": "…" }
```

`PATCH` accepts the editable fields only; `userId`, ids, and timestamps are
never client-settable. List endpoints paginate with an opaque cursor and a
`limit` (default 50, max 200), ordered by `updatedAt` descending like the
web UI.

### Postings & fit check

```
POST /api/v1/postings                       scope: postings:write
POST /api/v1/fit-check                      scope: fitcheck:run
POST /api/v1/applications/{id}/fit-check    scope: fitcheck:run
```

`POST /postings` is the Add-posting flow: `{ company, roleTitle, url?,
postingText?, payMin?, payMax? }` — or `{ url }` alone to fetch the text
server-side (SSRF-guarded, never auto-saved on fetch failure). Creates a
**Saved** application and runs the fit check against the caller's profile,
returning the application object including its verdict.

`POST /fit-check` is the ad-hoc form — a posting checked against the
caller's profile *without saving anything*:

```json
// request
{ "company": "Example Co", "roleTitle": "…", "postingText": "…", "payMin": null, "payMax": 130000 }
// response
{ "verdict": "Possible", "reasons": ["Pay max $130,000 is above your $95,000 floor (+)", "…"] }
```

Verdicts are the product's five: `Blocked`, `Poor fit`, `Possible`,
`Strong fit`, `Not checked` (no profile or no criteria yet) — deterministic,
rule-based, with a reason per rule, identical to the web fit check.

### Packets & export

```
GET /api/v1/applications/{id}/packet?format=json   scope: packets:generate
GET /api/v1/applications/{id}/packet?format=md     scope: packets:generate
GET /api/v1/export                                 scope: applications:read
```

`GET …/packet?format=json` returns **the packet v2 JSON unchanged** — the
same document the Prepare page downloads:

```json
{
  "packetVersion": 2,
  "generatedAt": "2026-11-21T…Z",
  "jobhuntApplicationId": 123,
  "atsDetected": "Greenhouse",
  "sourceUrl": "https://…",
  "instructions": ["Use ONLY the facts in this packet…", "Never click Submit…"],
  "posting":    { "company": "…", "title": "…", "url": "…", "location": "…", "payMin": 0, "payMax": 0, "text": "…" },
  "fitCheck":   { "verdict": "Strong fit", "reasons": ["…"] },
  "profile":    { "targetRoles": "…", "remotePreference": "…", "payFloor": 95000, "mustHaves": "…", "dealBreakers": "…", "skipCompanies": "…" },
  "standingAnswers": "…",
  "applicationStatus": { "status": "Applied", "dateSaved": "…", "dateApplied": "…", "nextAction": "…", "nextActionDue": "…", "notes": "…" },
  "resume":     { "name": "…", "fileName": "…", "notes": "…", "textIncluded": true, "truncated": false, "text": "…", "note": "…" },
  "atsFormMap": "…",
  "cautions":   ["…"],
  "readyPrompts": [{ "title": "Prepare me", "prompt": "…" }],
  "returnFormat": ["…"]
}
```

`format=md` returns the Markdown rendering (`Content-Type: text/markdown`).
`GET /export` returns the same JSON as the web UI's *Download my data*
(profile + all applications + resume metadata, no PDF bytes).

## 6. MCP server mapping

A separate, thin MCP server (its own small project, e.g.
`JobHuntHQ.Mcp/`) that speaks the Model Context Protocol over streamable
HTTP and calls the REST API — or the shared service layer — with the
**user's own token**. The MCP server holds no credentials of its own and
adds no capabilities the API doesn't have; in particular it inherits
Principle 1 structurally: there is no submit tool because there is no
submit endpoint to wrap.

**Tools** (each maps 1:1 to a REST call; token scopes apply unchanged):

| MCP tool | REST equivalent | Scope |
|---|---|---|
| `get_profile` | `GET /profile` | `profile:read` |
| `list_applications` | `GET /applications` (status/due filters) | `applications:read` |
| `get_application` | `GET /applications/{id}` | `applications:read` |
| `add_posting` | `POST /postings` | `postings:write` |
| `update_status` | `POST /applications/{id}/move` | `applications:write` |
| `run_fit_check` | `POST /fit-check` or `/applications/{id}/fit-check` | `fitcheck:run` |
| `get_packet` | `GET /applications/{id}/packet` | `packets:generate` |
| `get_export` | `GET /export` | `applications:read` |

Tool results return the same JSON shapes as REST. `get_packet` returns the
packet JSON as structured content so the calling assistant can work from it
directly — the packet's own instructions (facts only, flag unknowns, stop
before submission) travel inside it, as designed.

**Resources:**

| Resource URI | Content |
|---|---|
| `jobhunthq://brand` | `brand.md` — what the product is/isn't, voice, the never-submit principle |
| `jobhunthq://packet-spec` | The packet v2 format: section order, field meanings, the instruction contract |
| `jobhunthq://llms` | `wwwroot/llms.txt` — the product summary |

**Prompts:** the packet's ready prompts, exposed as MCP prompts so any
client can offer them: **Prepare me** (work the packet into a preparation
brief), **Draft my answers** (from standing answers only), **Tailor my
resume bullets** (resume bullets against the posting, facts unchanged), **Rehearse
me** (interview practice from the posting). Prompts take an application id
and internally call `get_packet` — they are conveniences over the same
data, not new powers.

**Transport & hosting:** streamable HTTP, same Azure footprint, behind the
same token auth (the client passes the user's token; the MCP server never
sees a password). stdio transport is a possible later add-on for local-only
clients, using the same token against the public API.

## 7. Phased rollout

1. **Phase 0 — this design.** Reviewed against `brand.md`; the never-submit
   line and the scope table are the parts that must not drift.
2. **Phase 1 — private beta, read-only.** Token creation UI (hashed
   storage, scopes, revoke), `GET` endpoints only (`profile`,
   `applications`, `export`, packet download). Owner + invited users.
   Exit criteria: a personal AI assistant completes a full read session
   (profile → pipeline → packet) using only a `read`-scoped token.
3. **Phase 2 — write scopes.** `POST /postings`, fit-check endpoints,
   `move`/`PATCH`/`DELETE`, `PUT /profile`. Rate limits and audit logging
   on from day one of writes. Exit criteria: the add-posting → fit-check →
   board-move loop works end-to-end via API alone.
4. **Phase 3 — MCP server.** Tools/resources/prompts per §6, shipped behind
   the same tokens. Exit criteria: the four prompts run against a real
   application from a stock MCP client.
5. **Phase 4 — public.** API docs page on the site (in the brand voice),
   token self-service open to all accounts, limits published. The docs page
   states the never-submit principle in its first paragraph — it is the
   feature.

## 8. Open questions (decide at build time)

- Token hashing scheme (salted SHA-256 vs HMAC with a Key Vault secret).
- Whether packet generation counts against a stricter quota for free
  accounts if hosting costs grow.
- Cursor pagination format (opaque base64 of `updatedAt,id` is fine).
- Whether `POST /postings` with a URL should ever auto-fill company/title
  from the fetched page (the web flow deliberately doesn't over-parse —
  keep the API the same unless evidence says otherwise).
