# Changelog

Every pull request, what it changed, and the commits that carried it. New entries go on top under
`Pending`; when the PR merges, the heading becomes its number and date.

The feature list these add up to is in [FEATURES.md](FEATURES.md); what is still planned is in
[ROADMAP.md](ROADMAP.md).

## Pending — `feat/search-values`

Search now looks inside the records, not only at their names.

- **The list no longer disappears while searching.** The match counter had taken the tree's `v-else`
  branch, so typing a query replaced the rows with a line of text — a regression from the polish
  round. The counter and the tree now share one branch and the tree keeps rendering.
- **Search matches attribute values.** `SN-0042` finds the record that carries it, `serial` finds the
  records that have that key at all, and a matched row shows the pair under its name
  (`Serial · SN-0042`) with the count reporting only rows that matched themselves, not the parents
  kept for context.

## Merged — `feat/ui-polish` (PR #8, 9 commits)

Guarded form, typed values, attributes of your own, duplicate, a filter and the committed inventory.

- **Unsaved work is protected.** Closing the sheet or picking another record on a desktop asks
  before dropping edits; the form says `Unsaved changes` while it differs from what was loaded.
  `bfd7400 feat(ui): ask before throwing edits away`
- **The list marks records that carry files.** One grouped query sets `AttachmentCount`; a paperclip
  appears on those rows. `17b4231 feat(api): tell the list which records carry files`
- **Attribute definitions have a data type.** `Constants.DataType` (Text, Number, Money, Date,
  LongText) with a migration and a one-time backfill of the seeded attributes; a “Data type” select
  in Configuration. `575d29b feat(attributes): an attribute definition carries a data type`
- **The item form follows the type.** Decimal keypad and separators for money, the device calendar
  for dates, a textarea for prose, and text that is no longer scrubbed to digits (`SN-0042` used to
  save as `-0042`). `93b4f50 feat(ui): typed values, attributes of your own, and a filter`
- **Attributes only one item needs.** “Add custom” adds a key of your own, stored on that item and
  rebuilt on load; a value without a name, or two rows with the same name, is refused.
- **Duplicate a record** from the overflow menu: same parent, icon and attributes, named
  `<name> (copy)`; files are not copied.
- **Filter inside the form** once there are more than five attributes.
- **Add a child from the open record**, so the parent does not have to be found in a list again.
  `90638db feat(ui): add a child from the record you are looking at`
- **The inventory is committed** and refreshed by `.githooks/pre-commit`, with `--no-stamp` keeping
  it byte-identical when nothing changed. `2c03125 chore: commit the inventory and keep it in step with the code`
- **Roadmap rewritten** to what is actually left. `89995d1 docs: the roadmap records what landed`,
  `a305759 fix(ui): put the attribute actions above the rows`

## #7 — Feat/UI polish · merged 2026-11-09 (2 commits, 8 files)

- **The list reads as records, not rows.** No more `0 - Add New` node (creation is a button and an
  empty state), 44px targets, hairline separators, tinted parents, child counts, chevrons on leaves,
  and `4 matches` when searching.
- **The form has a rhythm.** Sections (Details / Attributes / Attachments), the record's own name as
  the title, labels as words (`BuyPrice` → *Buy Price*), one header row for attributes, numbers
  formatted until focused and right-aligned, prose spanning the row.
- **Attachments are a compact list** with a 48px thumbnail instead of full-width cards.
- **Delete moved to an overflow menu** that names what it deletes; the footer is Reset and Save.
- **An empty pane says “Select an item to edit it”.**
- `25d23d9 docs: roadmap…`, `0ca39cc feat(ui): a clearer list, a real form rhythm and fewer traps`

## #6 — Photo opens the camera, a document the file manager · merged 2026-11-09 (1 commit, 2 files)

- A `Photo` attachment opens the camera (`capture="environment"`, “Take a photo” with a gallery
  option); every other type opens the file manager (“Select a file”). New attachments default to
  Photo when the catalogue has it.
- `e5aa36d feat(attachments): a photo opens the camera, a document opens the file manager`

## #5 — Attachments on disk · merged 2026-11-09 (1 commit, 18 files)

- **The original goes to disk, a preview in the row.** `media/items/{itemId}/{guid}{ext}` plus a
  320px JPEG (`ThumbnailBase64`, quality 72), 20 MB cap, SkiaSharp (MIT).
- `GET /api/attachment/{id}/file` streams the original with range support; deleting a row deletes the
  file; `FileBase64` → `ThumbnailBase64` with a migration and a one-time backfill.
- The database went from 16.4 MB to 221 KB once images stopped living in rows.
- `a472273` (branch `feat/attachments-on-disk`)

## #4 — Seed and mobile · merged 2026-11-09 (2 commits, 13 files)

- **The database is not in source.** `imc.db` untracked and ignored, migrations at startup, the
  configuration tree seeded on first run — items are never seeded.
- **Phone-first list and edit sheet**: the list is the page, the form is a full-height sheet with a
  pinned action bar above the safe area.
- `chore(db): stop tracking the SQLite file…`, `feat(ui): phone-first list and edit sheet`

## #3 — Attributes and attachments driven by the constants tree · merged 2026-11-09 (1 commit, 5 files)

- Attribute rows come from the `Attribute` constants: a label with children is a choice list,
  anything else is free text. Attachments follow the `AttachmentType` catalogue.
- `feat(ui): attributes and attachments driven by the constants tree`

## #2 — One tree editor instead of two copies · merged 2026-11-09 (1 commit, 4 files)

- `useTreeEditor` holds load / select / save / delete / dirty state; `TreeMasterDetail` renders the
  split view and the sheet; the pages keep only their own fields (348 → 84 lines for Configuration).
- `feat(ui): one tree editor instead of two copies`

## #1 — UI p0 · merged 2026-11-09 (6 commits, 14 files)

- **Runnable from a clean clone**: `npm ci`, dev server, no missing env var.
- **A feedback layer**: every failure shows the API's own message; every success confirms.
- **Dialog correctness**: validation actually runs, deletes ask for confirmation, buttons cannot
  double-post.
- **Honest state**: loading, empty and error states instead of a blank pane; valid attribute JSON.
- **Reachable from another machine**: API on `0.0.0.0:5173`, development CORS open, the client
  derives the API host from the page.
- **Pages named after their job**: `inventory` and `configuration`, `/` redirects to `/inventory`.
