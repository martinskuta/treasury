# Design guidelines

How Treasury's web app (`web/`) looks and feels. Every UI change follows this document; when something new doesn't fit it, extend this document in the same change instead of improvising.

The design is **calm, warm and airy**: a cream page, flat tonal panels, few lines, big quiet numbers, smooth charts, and one character, Frankie the cow. The original prototypes are the primary source when a detail isn't written down here: branch `prototype/design-refresh`, files `samples/ui/prototypes/cream-v3.html` (layout, UI components, charts, Transactions) and `samples/ui/prototypes/frankie-v4.js` (Frankie). Read them with `git show prototype/design-refresh:<path>`.

## Vocabulary

- A panel on a page is a **UI component** (CSS `.ui-component`). Never call it a "card": in Treasury a card is a payment card (an Account).
- Domain words come from `GLOSSARY.md` (Account, Multi-currency account, Owner, Transaction, Income, Expense, Transfer, Category, Tag, Uncategorised, Display name…), in UI copy as in code.
- The landing page is the **Dashboard** (route `/`). The **Monthly overview** is a separate report.

## Colour

Light theme only. All colours come from these tokens (CSS custom properties on `:root`); never hard-code a hex value in a component.

| Token | Value | Role |
|---|---|---|
| `--bg` / `--cream` | `#F0F4DC` | page background |
| `--panel` | `#FAFBF2` | UI component surface, inputs, chips |
| `--ink` | `#3A2A21` | primary text |
| `--muted` | `#9C8B7D` | secondary text, labels, axis text |
| `--brown` | `#714D3A` | brand, primary buttons, active nav, Housing-like dark accents |
| `--lime` | `#C6E36B` | **Income** fills (bars, chart line `--lime-line` `#ADCF45`) |
| `--lime-soft` | `#E3EFB4` | Income backgrounds, Tag pills |
| `--inc-ink` | `#5F7D16` | Income text and positive amounts |
| `--coral` | `#F46060` | **Expense** fills and chart line |
| `--coral-soft` | `#FBD9D3` | Expense / Uncategorised backgrounds |
| `--exp-ink` | `#D24646` | Expense text, negative amounts, Uncategorised label |
| `--teal` | `#95C8CE` | **Transfer** fills, focus ring |
| `--teal-soft` | `#D8ECEE` | Transfer backgrounds |
| `--tr-ink` | `#3E8790` | Transfer text and amounts |
| `--track` | `#EAEDD5` | empty part of progress bars |
| `--grid` | `#ECEFD9` | chart gridlines |
| `--nav-ink` | `#7B6758` | nav link text (between `--muted` and `--ink`) |
| `--beige-soft` | `#EFEBDB` | Expense icon background in Transaction rows |
| `--welcome-tint` | `#EEF5CC` | end of the Dashboard welcome strip's gradient |
| `--brown-tint` / `--brown-hover` | brown at 7% / 6% | segmented track, secondary button inside a UI component / hover |
| `--shadow-tiny` / `--shadow-float` | brown-tinted shadows | selected segment / floating bars (bulk edit) |

- **Meaning is fixed**: lime = Income / positive, coral = Expense / negative / needs attention, teal = Transfer / neutral movement, brown = brand and primary action. Don't reuse a semantic colour for decoration.
- Text on a colour uses its `-ink` variant, never the fill colour itself (lime and teal fills are too light for text).
- **Category series** (donuts, stacked bars) use, in order: `--c1 #714D3A`, `--c2 #95C8CE`, `--c3 #C6E36B`, `--c4 #F59A8E`, `--c5 #C7A48C`, `--c6 #C9E3E6`, `--c7 #DCD9B0`.
- Frankie's gold (`#E0B23A`) belongs to Frankie and coins only.

## Typography

- **Inter** (400, 500, 600, 700) for everything, `letter-spacing: -0.006em`, base 15px, line-height 1.45, antialiased.
- Headings are 600, slightly tight (`-0.02em`): page title 1.45rem; UI component title 0.95rem.
- Labels and legends: 0.78–0.85rem, `--muted`, weight 500.
- **Amounts**: tabular figures (`font-variant-numeric: tabular-nums`), Swiss format (`1'234.50`, dates `20.11.2026`). Key amounts are large and quiet: 600 weight, `-0.03em`, with the currency code small and muted before the number (`.cur`: 0.42em) and the cents de-emphasised (`.dec`: 0.62em, 45% opacity). Sign colour: `--inc-ink` positive, `--exp-ink` negative, `--tr-ink` Transfers.

## Layout and spacing

- **App shell**: fixed left sidebar (216px) with the brand (brown rounded square + "Treasury"), nav links (rounded 12px, active = `--panel` background) and the signed-in Member at the bottom. Below 900px the sidebar becomes a top bar with a horizontally scrolling nav.
- **Content**: max-width 1300px. Pages are made of UI components on a grid with a small, consistent gap: `--gap: 14px`.
- **UI component**: `--panel` background, radius `--r: 22px`, padding `--pad: 22px 24px` (20px on phones), **no border, no heavy shadow**. Separate content inside with spacing, not divider lines.
- **Dashboard** is a bento grid: 3 columns → 2 below 1150px → 1 below 700px. Order: welcome strip (full width) · This month · Spending by Category · Accounts · Last 6 months (full width) · Latest Tag · Uncategorised.
- Airy means generous padding inside UI components and few elements per component. When a component feels busy, remove secondary labels before shrinking type.
- Responsive down to 320px; check 390px.

## Controls

- **Buttons**: pills (`border-radius: 99px`, padding 10px 20px, weight 600). Primary = `--brown` with cream text; secondary = `--panel` on cream or a soft tint.
- **Inputs and selects**: pills on `--panel`, no border, focus = 2px `--teal` ring, custom chevron in `--muted`. Inside a UI component (already `--panel`) they sit on `--bg` instead, and secondary buttons on `--brown-tint`, so they stay visible.
- **Segmented controls** (e.g. mood switcher, toggles): a pill track tinted `rgba(113,77,58,.07)`; the selected segment is `--panel` with a tiny shadow.
- **Chips / Tag pills**: small (0.72rem, 600), `--lime-soft` background with `--inc-ink` text for Tags; Owner hints as muted chips.
- **Transaction rows** (Transactions, Uncategorised): grouped by day under a small muted date header; a round 38px icon tinted by kind (Income lime-soft, Expense neutral beige, Transfer teal-soft, Uncategorised coral-soft), Display name (or description) 600, counterparty / Category / Account · Owner on a muted second line, amount right-aligned with an optional muted original amount below. Row actions (Edit, Link…, Delete) are small text links that appear on hover/focus with a mouse and sit on their own line on phones; a selected row gets a `--brown-tint` background and bulk edit floats at the bottom (`--shadow-float`). Filters are unlabelled pills (the first option names the filter, plus an accessible name) and fold under a Filters toggle below 768px. Styles: `web/src/styles/transactions.css`.

## Charts

Inline SVG, no chart library, styled from the tokens. Charts are smooth and soft, never spiky or boxed:

- **Trend lines** (Last 6 months and any time series): smooth curves with horizontal tangents at each point so they never overshoot (cubic Bézier with control points at the horizontal midpoint), stroke 3px with round caps, a vertical gradient fill under each line from the series colour at 38% opacity to 0 at 90% height, an end marker (`--panel` circle with the series stroke) on the latest point, 3–4 faint `--grid` gridlines, muted axis labels, the current month label in `--ink`. Income = `--lime-line`, Expense = `--coral`.
- **Donuts and rings**: thick strokes with round line caps and small gaps between segments; the total sits in the centre in `--ink`, with a small muted currency label.
- **Bars and progress**: 10–12px pill tracks (`--track`) with fully rounded fills; stacked bars have 4px gaps and rounded segments.
- Legends: 8px dots + muted labels, values right-aligned in tabular figures.
- Every chart has a text fallback (`<title>` per point or segment, or the numbers shown beside it).

## In code

The foundation lives in `web/src/styles/` (`tokens.css`, `base.css`, `shell.css`, `controls.css`, `charts.css`, imported by `index.css`) and `web/src/components/`; each file starts with a short usage note.

- UI component: `<UiComponent title actions>` or `.ui-component`; `.ui-grid` / `.stack` for the gap.
- Amounts: `<Amount value currency signed tone size>` for key amounts; `formatAmount()` with `.num` / `.amount` in lists.
- Controls: `.button-primary`, `.button-secondary`, `.button-link`, `.button-small`; `FormField` / `SelectField` or `.input`; `<SegmentedControl>`; chips `.chip`, `.chip-tag` (`.tag-chip`), `.chip-uncategorised`.
- Charts (`components/charts`): `<TrendChart>`, `<Donut>`, `<ProgressBar>`, `<StackedBar>`, `<ChartLegend>`, colours via `toneColors` and `seriesColor(i)`.
- Text helpers: `.page-title` / `.page-header`, `.section-title`, `.label`, `.muted`, `.inc` / `.exp` / `.tr`.

## Frankie

Frankie is Treasury's mascot: a white Swiss cow with brown patches, a pink snout, a gold cowbell, a coin slot on its back and a plug under its belly — a cow-shaped money box. Its name nods to the Swiss franc.

- **Where**: only at the left of the Dashboard's welcome strip, next to the greeting and this month's net. Keep it special: no Frankie in lists, forms or the nav.
- **Moods**: `neutral` (sits, blinks, chews cud with tiny green grass pixels falling), `happy` (coins slide into the slot, gets slightly rounder, dog-like tail wag and happy wiggle), `very-happy` (the happy wiggle, then the "heel kick" run: bucks, trots off, peeks back, returns and sits), `sad` (the plug pops open, coins fall out of it, gets slightly thinner, droopy ears, frown). Which mood shows is decided by the app, not by Frankie.
- **Room**: during `very-happy` Frankie leaves its own box; give its host about 2.2× its width and a little headroom so it isn't clipped.
- **Motion**: animations are gentle loops; under `prefers-reduced-motion: reduce` each mood is a still pose.
- Frankie's drawing and animations are fixed by the prototype; change them only deliberately, as a design decision.

## Motion

Subtle and purposeful: short ease-out transitions (150–250ms) on hover and state changes, nothing that moves on its own except Frankie. Respect `prefers-reduced-motion` everywhere.

## Accessibility

- Body text contrast at least WCAG AA on `--panel` and `--bg`; use the `-ink` colour variants for coloured text.
- Colour is never the only signal: amounts carry a sign, Uncategorised says "Uncategorised", chart series have legends.
- Interactive elements are reachable by keyboard with a visible focus ring (`--teal`).
