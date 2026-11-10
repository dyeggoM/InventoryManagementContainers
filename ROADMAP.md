# Roadmap

What is deliberately not built yet, in the order it is worth doing.

## Done

- **Per-item custom attributes.** A row can be added with a key the catalogue does not define; it is
  stored in that item's `Attributes` JSON and survives a reload. No global side effect: the label
  belongs to the item that has it, and a later decision to promote it to Configuration is a manual
  one, which is the answer to the open question this item used to carry.
- **Typed attribute values.** `Constants.DataType` (`Text`, `Number`, `Money`, `Date`, `LongText`)
  drives the control, the keypad, the alignment and the formatting. Set in Configuration, per
  attribute; null means Text so old rows keep working.
- **Duplicate a record.** Kits repeat: the overflow menu copies parent, icon and attributes into a
  new record named `<name> (copy)`. Files are not copied.
- **Installable on the home screen.** PWA mode, manifest, icons and a service worker that serves the
  shell offline.
- **Tests with teeth.** `GetById` was returning no children at all (the two failures on `main` were
  a real bug, not flaky tests); nine API integration tests now drive the app over HTTP against a
  throwaway database, and Vitest covers the tree editor's state machine and the attribute editor.
- **Uploads without base64.** A multipart endpoint takes the file as bytes; the form saves the
  record first and uploads against the id it gets back.
- **Search by attribute value.** `SN-0042` finds the record that carries it and shows the pair under
  the name; the count only counts rows that matched themselves.

## 1. Authentication

The API has no authentication: `[Authorize]` is commented out, CORS is open in development and the
service binds `0.0.0.0` for the LAN. On the workshop network that is a conscious trade-off; before
the app leaves it, a shared token (or ASP.NET Identity, which the context already inherits from)
has to close the door. Deferred by decision until the UI and the data model settle.

## 2. Bulk entry from a template

Entering a kit still means one record at a time. Duplicate covers the repeated-attributes case; the
next step is a CSV/Excel template the user fills in (parent, children, attributes per row, one column
per attribute key) and uploads, with a preview of what will be created before anything is written.
Waits on authentication: an import endpoint is the last thing to leave open.
