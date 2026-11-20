import type { Attributes } from './items';

/**
 * The attribute map of an item, as the form and the payload see it.
 *
 * `Items.Attributes` is a JSON object whose **keys are attribute codes** —
 * `{"Brand": "Bosch", "Type": "Part"}` — and whose values are codes too when the attribute is a
 * list. The form edits rows and the API stores that JSON, so the two translations live here, in
 * one place, and a label never enters either direction: that is what makes a rename in
 * Configuration free of data migrations.
 */

/** The stored map, or null when the stored value is not a JSON object. */
export function readAttributeMap(raw: string | null | undefined): Record<string, string> | null {
  if (!raw) return {};
  try {
    const parsed: unknown = JSON.parse(raw);
    return parsed && typeof parsed === 'object' ? (parsed as Record<string, string>) : null;
  } catch {
    return null;
  }
}

/** One empty row per catalogue attribute: a new record, with the form ready to be filled. */
export function blankRows(codes: string[]): Attributes[] {
  return codes.map((code) => ({ code, value: '' }));
}

/**
 * The rows the form shows for a stored record: one per catalogue attribute, in the catalogue's
 * order, plus any key the record carries that the catalogue does not define — a value typed on
 * that item alone must survive a reload. `malformed` says the stored JSON had to be reset.
 */
export function attributeRows(
  raw: string | null | undefined,
  codes: string[],
): { rows: Attributes[]; malformed: boolean } {
  const parsed = readAttributeMap(raw);
  if (!parsed) return { rows: blankRows(codes), malformed: true };

  const rows = codes.map((code) => ({ code, value: parsed[code] ?? '' }));
  const extra = Object.keys(parsed).filter((key) => !codes.includes(key));
  return {
    rows: [...rows, ...extra.map((code) => ({ code, value: parsed[code] ?? '' }))],
    malformed: false,
  };
}

/**
 * The rows as the API stores them.
 *
 * A row with no code yet is not written (an empty key identifies nothing), and neither is an empty
 * value: the absence of a value is the absence of the key, which is what keeps the round trip
 * **identity** — `{"Brand":"Bosch"}` in, `{"Brand":"Bosch"}` out, whatever the catalogue's labels
 * say and however many attributes the form rendered.
 */
export function attributesJson(rows: Attributes[]): string {
  const record: Record<string, string> = {};
  for (const row of rows) {
    const value = row.value ?? '';
    if (row.code && value !== '') record[row.code] = value;
  }
  // A serializer, never string concatenation: a value with a quote or a newline used to produce
  // invalid JSON that the API stored verbatim.
  return JSON.stringify(record);
}
