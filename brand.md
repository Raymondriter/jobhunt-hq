# JobHunt HQ — brand sheet

The one-page brand guideline. If it isn't decided here, keep it simple and
consistent with what's already on the site.

## Name & tagline

- **Name:** JobHunt HQ
- **Tagline:** *Track every application. Prepare with your own AI. Apply as yourself.*
- **Short line (footer/secondary):** Track every application, never miss a follow-up.

## What JobHunt HQ is / is not

**Is:** a job application tracker that prepares and visualizes. It checks
postings against *your* criteria, builds a preparation view per application,
and exports an AI packet your own AI assistant can work from.

**Is not:** an auto-apply tool. It never submits applications, never logs
into job boards or employer sites as the user, never scrapes on the user's
behalf, and never invents experience or answers. The user reviews and
submits everything themselves — this is the product's core promise, and it
goes in the trust block everywhere the product is described.

## Palette

Derived from the live site (`wwwroot/css/site.css`). One accent family;
the status hues are Bootstrap 5's, used for pipeline pills, board columns,
and dashboard cards.

| Role | Hex | Notes |
|---|---|---|
| Accent (primary) | `#0f5e8c` | Links, buttons, the JH mark, brand accent |
| Accent dark | `#0b4769` | Hover states, pitch text |
| Ink | `#1c2430` | Body text, wordmark |
| Muted | `#5a6572` | Secondary text, tagline |
| Line | `#dde3e9` | Borders, dividers |
| Panel | `#f6f8fa` | Card / section backgrounds |
| Badge | `#e8edf1` | Neutral pill background |

Status palette (fit verdicts reuse the same hues):

| Status | Hex | | Fit verdict | Hex |
|---|---|---|---|---|
| Saved | `#6c757d` | | Strong fit | `#198754` |
| Applied | `#0d6efd` | | Possible | `#0aa2c0` |
| Screening | `#0aa2c0` | | Poor fit | `#ffc107` (dark text) |
| Interview | `#e0a800` / pill `#ffc107` | | Blocked | `#dc3545` |
| Offer | `#198754` | | Not checked | `#f8f9fa` (muted text) |
| Rejected | `#dc3545` | | | |
| Ghosted | `#212529` | | | |
| Withdrawn | `#adb5bd` / pill `#f8f9fa` | | | |

## Typography

- **Stack:** `"Segoe UI", system-ui, -apple-system, "Helvetica Neue", Arial, sans-serif` — the system stack the site already ships; no webfont downloads.
- Headings are the same family, bold, tight letter-spacing on large sizes.
- Body: 16px, line-height 1.55.

## Voice

Plain, honest, no hype. Write like a competent friend who has done a lot of
applying, not like a growth startup.

- Say what the product does and doesn't do, in those words.
- **Never** use automation-hype words for JobHunt HQ: no "auto-apply",
  "spray", "bot", "10x your applications", "land interviews while you sleep".
- The user's AI is *theirs* — "your own AI", "the AI you already use".
- Numbers over adjectives; concede trade-offs honestly (a spreadsheet is
  free; an auto-apply bot is faster and riskier).

## Logo

- **The mark:** "JH" in white bold on a rounded square (corner radius ≈ 20%
  of the mark's size) filled with the accent `#0f5e8c`. On the site it is
  2.1rem with an 8px radius (`wwwroot/brand/logo.svg` is the standalone
  lockup: mark + "JobHunt HQ" wordmark in ink).
- **Clear space:** at least the height of the "J" on all sides.
- **Minimum size:** 16px for the mark alone (favicon territory).
- **Don'ts:**
  - Don't build the logo from stock imagery or icon-library art — stock
    licences forbid trademark/logo use, and the mark must stay ownable.
  - Don't stretch, rotate, outline, or add shadows/gradients.
  - Don't recolor the mark outside the palette (white-on-accent, or
    accent-on-white, or ink mono — nothing else).
  - Don't put the mark on a busy or low-contrast background.
  - Don't change the wordmark's typeface or letter-spacing.
