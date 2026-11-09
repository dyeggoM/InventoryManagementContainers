# Features

What the app does today, by area. Every line here is implemented and can be exercised in the running
app; the history of how it got here is in [CHANGELOG.md](CHANGELOG.md), what is planned is in
[ROADMAP.md](ROADMAP.md).

## Inventory

- A tree of records with children nested under their parent; a record can hold any depth.
- Create from the list (“New item”, a floating button on phones), **add a child straight from the
  open record**, or **duplicate** one (parent, icon and attributes carried over, named `<name> (copy)`).
- Search matches the record's name **and its attribute values**: `SN-0042` finds the item that
  carries it, `serial` finds the items that have that key, and a matched row shows the pair under the
  name. The count reports rows that matched on their own, and the panel says when nothing matches.
- Rows show a child count while collapsed and a paperclip when the record carries files.
- Delete from an overflow menu that names what it deletes, with confirmation.
- Empty states everywhere: no records yet, nothing matches, and “Select an item to edit it”.

## Attributes

- The set of attributes comes from Configuration (`Attribute` constants); a label with children there
  becomes a choice list, anything else is free text.
- **Typed values**: Text, Number, Money, Date or LongText per attribute definition. Money and Number
  get the decimal keypad, digits only, right alignment and `10,000,000` while unfocused; Date opens
  the device calendar; LongText is a textarea across the row.
- **Attributes of your own**: a key the catalogue does not define can be added to a single item and
  is kept with that item only. A value without a name, or two rows with the same name, cannot be
  saved.
- A filter inside the form once there are more than five attributes, matching name or value.
- camelCase keys read as words (`BuyPrice` → *Buy Price*) without changing what is stored.

## Attachments

- A `Photo` opens the camera (`Take a photo`, with a gallery option); every other type opens the file
  manager (`Select a file`). New rows default to Photo when the catalogue has it.
- The original is stored on disk under `media/items/{itemId}/`, a 320px JPEG preview in the row
  (quality 72), 20 MB limit.
- “Open original” streams the file from `GET /api/attachment/{id}/file` (range requests supported) so
  a large photo opens in the phone's viewer instead of being decoded into the page.
- Deleting an attachment or a record removes the files.

## Configuration

- A tree of constants (FieldType, AttachmentType, Category, Brand, Classification, Type, Attribute)
  with the same create / add child / duplicate / delete behaviour as inventory records.
- Attribute definitions carry a **data type**, with a one-line explanation of each choice.
- Seeded on first run (249 nodes) so a fresh database is usable immediately; items are never seeded.

## Phones and the local network

- The list is the page and the form is a full-height sheet with a pinned footer above the safe area;
  the action that matters is never below the fold.
- Unsaved edits are protected: closing the sheet, or switching record on a desktop, asks first.
- The API binds `0.0.0.0:5173` and the UI `0.0.0.0:9000` in the `lan` profile, development CORS is
  open, and the client derives the API host from the page it was served from — so the same build
  works from `localhost` and from the LAN address.

## Installable

- The app is a PWA: add it to the home screen from the browser and it opens without the address bar
  (`display: standalone`), with its own icon (gradient monogram, maskable variants for Android).
- A service worker caches the shell, so opening it without a network shows the interface and an
  honest `Cannot reach the API` instead of a blank page.
- When a new version is deployed, a notification offers **Reload** rather than reloading under an
  open form.

## Data

- SQLite through EF Core; migrations run at startup, so a clone builds its own schema.
- The database file is not committed; deleting it starts over from the seed.
- Images and files live on disk, not in rows.

## Repository tooling

- `AI_INVENTORY.md` — a symbol-level map of both projects (321 symbols, call edges, `path:line`
  anchors) committed with the code and refreshed by `.githooks/pre-commit` from the staged files.
- `ROADMAP.md` — what is deliberately not built yet.
- `CHANGELOG.md` — every pull request and what it changed; this file — the resulting capability list.
- Checks used before each commit: `vue-tsc`, `eslint`, `dotnet build`, `dotnet test`.
