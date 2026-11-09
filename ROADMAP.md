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
- **Search by attribute value.** `SN-0042` finds the record that carries it and shows the pair under
  the name; the count only counts rows that matched themselves.

## 1. Uploads without base64

Attachments are posted as a data URL inside JSON, which is ~33 % overhead and holds the whole photo
in memory on the phone. A multipart endpoint (and `capture="environment"` straight on the input)
would stream the file and let the server thumbnail it as it lands.

## 2. Tests

The API has `Testing/` (repository, attachment storage, thumbnail rules). The UI has none:
`useTreeEditor`, the attribute editor (typed rows, custom keys) and the attachment editor deserve
Vitest tests before the next refactor, and the controllers need integration tests over the real
endpoints.

## 3. Bulk entry

Entering a kit still means one record at a time. Duplicate covers the repeated-attributes case;
the next step is adding several children to a parent in one pass (a small queue in the sheet), then
importing a CSV.
