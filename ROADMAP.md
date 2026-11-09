# Roadmap

What is deliberately not built yet, in the order it is worth doing.

## 1. Per-item custom attributes

The attribute set is global: `Attribute` constants define the labels every item shows, and the
form always renders all of them. That breaks down when one item needs a field the rest do not — a
serial number for a single drum, a locker number for one kit — because adding it to the `Attribute`
constant would show it on every item, empty.

Shape of the change:

- **Storage.** An `ItemAttribute` table (`ItemId`, `Label`, `Value`, `SortOrder`) next to the
  existing `Item.Attributes` JSON, or the JSON alone if we accept losing per-attribute ordering.
  Known labels keep coming from the constants tree; item-only labels live in the row.
- **Form.** "Add attribute" offers the catalogue *and* a "new attribute for this item" entry. The
  value control stays data-driven: a label with children in the constants tree is a choice list,
  anything else is free text.
- **Save.** The JSON payload keeps working for known attributes; item-only ones travel as their own
  array so the API can validate them separately.
- **Search.** Once items carry arbitrary attributes, search should look at them too, which is the
  moment to decide whether this becomes a real filter instead of a substring match.

Open questions before building it: does a label invented on one item become global after first use,
and do custom values need types (see below) or stay strings?

## 2. Typed attribute values

Values are strings, so the form guesses: a value that looks numeric gets the decimal keypad and
thousand separators. Prices, dates and booleans deserve a type on the `Attribute` constant
(`number`, `money`, `date`, `text`, `list`) so validation, formatting and sorting stop being
heuristics.

## 3. Uploads without base64

Attachments are posted as a data URL inside JSON, which is ~33 % overhead and holds the whole photo
in memory on the phone. A multipart endpoint (and `capture="environment"` straight on the input)
would stream the file and let the server thumbnail it as it lands.

## 4. Install as an app

The UI is phone-first but still a browser tab. A manifest plus a service worker makes it installable
on the home screen and gives a cached read of the last inventory when the phone is offline.

## 5. Tests

The API has `Testing/` (repository and attachment storage). The UI has none: `useTreeEditor`, the
attribute and attachment editors deserve Vitest tests before the next refactor, and the controllers
need integration tests over the real endpoints.

## 6. Bulk entry

Entering a kit means retyping the same attributes for every part. "Duplicate item" (or "add like
this") in the tree would remove most of that typing on a phone.
