import { api } from './api';
import { asText } from './utilities';

/**
 * The spreadsheet import (see `ImportController` / `ItemImporter`). Two calls, both multipart and
 * both admin:
 *
 *   POST /api/import/preview   fields `file`, `corrections`     — reads the file, writes nothing
 *   POST /api/import           fields `file`, `includeYellow`, `skipCodes` (repeated), `corrections`
 *
 * The panel shows the preview first and only then decides: red rows never go in, a yellow row goes
 * in unless the user unticks it, and a green row always does.
 *
 * Both calls take the same `corrections` array, because the server judges the file *after* applying
 * them: a red row becomes importable when its description arrives, and the preview must show that
 * before the import claims it. The array is JSON in one form field, never a part per row.
 *
 * Nullable fields are **absent** when the API has nothing to say (a row without a brand), so every
 * one of them is optional here and read through a guard instead of a bare access.
 */
export type ImportStatus = 'green' | 'yellow' | 'red';

const STATUSES: readonly ImportStatus[] = ['green', 'yellow', 'red'];

export interface ImportItem {
  /**
   * Unique inside one preview: the tree keys off it and the selection runs off it. A row number
   * repeats across sheets, so the sheet is part of the key.
   */
  key: string;
  label: string;
  code: string | null;
  brand: string | null;
  reference: string | null;
  serial: string | null;
  notes: string | null;
  dimensions: string | null;
  status: ImportStatus;
  /** Why the row is yellow or red; empty for a green one. */
  reasons: string[];
  /** The line in the sheet it came from, as the file numbers it. */
  row: number;
  /**
   * The spreadsheet sheet the row came from (`STOCK`, `CASES`, or the CSV file name uppercased).
   * The tree groups by category and subcategory now, so this is what keeps the original layout of
   * the file traceable. Absent when the reader did not record one. It is also the key into
   * `ImportPreview.sourceHeaders` and the field a correction must carry: row numbers repeat across
   * sheets, and a correction without it is refused as ambiguous.
   */
  source?: string | null;
  /**
   * The row exactly as the file wrote it: one entry per column of its header row, keyed by that
   * header. **Only sent for a row that is not green** — a green row needs no correction, so the API
   * omits it and the panel never shows the file for one.
   */
  raw?: Record<string, string> | null;
}

/**
 * One row the user corrected before importing. `source` + `row` identify it — the pair, never the
 * row alone: row 57 exists in more than one sheet of the same file.
 *
 * Every other field is optional and **absent means "leave what the file says"**. `label` is the
 * description on the wire: the API maps it to the row's `description` cell, which is the field that
 * decides whether a red row becomes importable.
 */
export interface ImportCorrection {
  /** The sheet the row came from, exactly as `ImportItem.source` spells it. */
  source?: string | undefined;
  /** The line number the file gives the row, the one the panel prints as `fila 57`. */
  row: number;
  label?: string;
  code?: string;
  /**
   * The subcategory as the file writes it. A corrected one places the row exactly as if the file
   * had said it, which can move the row to another group and another category.
   */
  subcategory?: string;
  brand?: string;
  reference?: string;
  serial?: string;
  notes?: string;
  dimensions?: string;
  /**
   * The item's attribute set, keyed by the label Configuration gives the attribute.
   *
   * The map is a **patch**, not a snapshot, and the API reads the two cases apart: a key that is
   * absent leaves the file's value alone, and a key sent as `""` removes the attribute. That is what
   * lets the editor say "I did not touch this" and "I cleared this" without inventing a third state.
   *
   * Two labels do not belong here. `Code` is governed by `code`, the one property the uniqueness
   * check validates, and a disagreeing `attributes.Code` is reported instead of written; and a
   * correction named after one of the seven fields the importer derives is applied to that cell, so
   * `Brand` here is the same thing as `brand` above.
   */
  attributes?: Record<string, string>;
}

/**
 * The API's recommendation about *where* a group went, when the name it read was not in the
 * configuration and a similarity match was close enough to propose one. `name` on the group is
 * already the proposed label: the rows really are placed there, so this is advice, never a rename.
 */
export interface ImportSuggestion {
  /** The known subcategory the reader compares against. */
  label: string;
  /** The categoría that subcategory hangs from. */
  category: string;
  /** How close the match was, 0-100. */
  percent: number;
}

export interface ImportGroup {
  name: string;
  status: ImportStatus;
  /**
   * Why this group landed where it did, when a rule decided it: `«CONGA DOBLE» se coloca en «BASE
   * CONGA DOBLE»`, `«CASE PARA CONGAS» no está en la configuración: se coloca en «Other»…`. Absent
   * for a group the reader placed by name alone, which is the common case.
   */
  note?: string | null;
  /**
   * The similarity proposal behind a `note` that proposes something. Absent for most groups — the
   * normal case — so the panel reads it through this optional field, never by parsing `note`.
   */
  suggestion?: ImportSuggestion | null;
  green: number;
  yellow: number;
  red: number;
  items: ImportItem[];
}

export interface ImportSheet {
  name: string;
  status: ImportStatus;
  green: number;
  yellow: number;
  red: number;
  /** Block titles the reader found between records: not records, so not an import error. */
  skippedSections: number;
  groups: ImportGroup[];
}

export interface ImportSummary {
  rows: number;
  sections: number;
  green: number;
  yellow: number;
  red: number;
  importable: number;
  skipped: number;
}

export interface ImportIssue {
  sheet: string;
  /** 0 when the reader could not tie the issue to a line. */
  row: number;
  level: string;
  message: string;
}

export interface ImportPreview {
  sheets: ImportSheet[];
  summary: ImportSummary;
  issues: ImportIssue[];
  /** Alias corrections the reader applied, e.g. `«GTAR COMBO» → «GTR COMBO»`. */
  normalizations: string[];
  /**
   * The file's own header row, per sheet: `{ "BACKLINE": ["CONSECUTIVO", "SUB CATEGORIA", …] }`.
   * It is what the row editor prints above the values, in the file's column order.
   */
  sourceHeaders: Record<string, string[]>;
  /**
   * The corrections the server refused and why (`Fila 57: es ambigua; indique source`). A refused
   * correction that nobody renders looks exactly like a button that did nothing, so it is shown.
   */
  correctionIssues: string[];
}

export interface ImportRequest {
  /** Yellow rows go in as a class. Defaults to true, which is what the API assumes. */
  includeYellow?: boolean;
  /** One entry per yellow code the user unticked. The API keys on codes, not on rows. */
  skipCodes?: string[];
  /**
   * Every correction made in this session, so the import writes what the user fixed instead of what
   * the file says. The server applies them before judging, exactly as it does for the preview.
   */
  corrections?: ImportCorrection[];
}

/** What `POST /api/import` answers: what it wrote, and what it left out. */
export interface ImportOutcome {
  createdSheets: number;
  createdGroups: number;
  createdItems: number;
  skippedRed: number;
  skippedYellow: number;
  skippedExisting: number;
  includeYellow: boolean;
  /** How many corrections the server accepted and applied to the rows it wrote. */
  corrected: number;
  /** The corrections the import refused, with the reason. Empty when every one was applied. */
  correctionIssues: string[];
}

/** A colour the UI does not know is read as red: red is the one that never gets imported. */
function toStatus(value: unknown): ImportStatus {
  const text = asText(value).trim().toLowerCase();
  return STATUSES.find((status) => status === text) ?? 'red';
}

function toNumber(value: unknown): number {
  if (typeof value === 'number' && Number.isFinite(value)) return value;
  const parsed = Number(asText(value));
  return Number.isFinite(parsed) ? parsed : 0;
}

function toNullableText(value: unknown): string | null {
  const text = asText(value).trim();
  return text ? text : null;
}

function toReasons(value: unknown): string[] {
  if (!Array.isArray(value)) return [];
  return value.map((reason) => asText(reason).trim()).filter((reason) => reason.length > 0);
}

function toStringList(value: unknown): string[] {
  if (!Array.isArray(value)) return [];
  return value.map((entry) => asText(entry).trim()).filter((entry) => entry.length > 0);
}

/**
 * The row as the file wrote it, keyed by its own header. An empty cell is kept as an empty string on
 * purpose: the editor shows one cell per column, so a missing value must leave its column in place
 * instead of shifting the rest of the row left.
 */
function toRawCells(value: unknown): Record<string, string> | null {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return null;
  const cells: Record<string, string> = {};
  for (const [header, cell] of Object.entries(value as Record<string, unknown>)) {
    const text = asText(cell);
    if (header.trim().length > 0) cells[header] = text;
  }
  return Object.keys(cells).length > 0 ? cells : null;
}

/** The file's header row, per sheet. A sheet without one is simply absent from the map. */
function toHeaders(value: unknown): Record<string, string[]> {
  const headers: Record<string, string[]> = {};
  for (const [sheet, list] of Object.entries(record(value))) {
    const columns = toStringList(list);
    if (columns.length > 0) headers[sheet] = columns;
  }
  return headers;
}

function record(value: unknown): Record<string, unknown> {
  return value && typeof value === 'object' ? (value as Record<string, unknown>) : {};
}

function array(value: unknown): unknown[] {
  return Array.isArray(value) ? value : [];
}

function toItem(raw: unknown, sheet: string, used: Set<string>): ImportItem {
  const source = record(raw);
  const row = toNumber(source.row);
  // Two rows claiming the same number would give two tree nodes the same key: keep the key unique.
  const base = `${sheet}#${row}`;
  let key = base;
  for (let suffix = 2; used.has(key); suffix += 1) key = `${base}~${suffix}`;
  used.add(key);

  return {
    key,
    label: asText(source.label).trim(),
    code: toNullableText(source.code),
    brand: toNullableText(source.brand),
    reference: toNullableText(source.reference),
    serial: toNullableText(source.serial),
    notes: toNullableText(source.notes),
    dimensions: toNullableText(source.dimensions),
    status: toStatus(source.status),
    reasons: toReasons(source.reasons),
    row,
    // `source.source` is the sheet the row came from: the record is `source`, the field is `source`.
    source: toNullableText(source.source),
    raw: toRawCells(source.raw),
  };
}

function toGroup(raw: unknown, sheet: string, used: Set<string>): ImportGroup {
  const source = record(raw);
  return {
    name: asText(source.name).trim(),
    status: toStatus(source.status),
    // Only a rule-decided placement carries one; absent and empty both read as "nothing to explain".
    note: toNullableText(source.note),
    suggestion: toSuggestion(source.suggestion),
    green: toNumber(source.green),
    yellow: toNumber(source.yellow),
    red: toNumber(source.red),
    items: array(source.items).map((item) => toItem(item, sheet, used)),
  };
}

/**
 * A proposal with no label is not a recommendation the panel can show, and the API omits the whole
 * object in the common case, so anything incomplete reads as "no suggestion" instead of a blank chip.
 */
function toSuggestion(value: unknown): ImportSuggestion | null {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return null;
  const source = record(value);
  const label = toNullableText(source.label);
  if (!label) return null;
  return {
    label,
    category: toNullableText(source.category) ?? '',
    // Clamped to the range a percentage can mean, so a stray 9400 never becomes "9400%".
    percent: Math.min(100, Math.max(0, Math.round(toNumber(source.percent)))),
  };
}

function toSheet(raw: unknown): ImportSheet {
  const source = record(raw);
  const name = asText(source.name).trim();
  // One set per sheet: the key carries the sheet name, so items cannot collide across sheets either.
  const used = new Set<string>();
  return {
    name,
    status: toStatus(source.status),
    green: toNumber(source.green),
    yellow: toNumber(source.yellow),
    red: toNumber(source.red),
    skippedSections: toNumber(source.skippedSections),
    groups: array(source.groups).map((group) => toGroup(group, name, used)),
  };
}

function toSummary(raw: unknown): ImportSummary {
  const source = record(raw);
  return {
    rows: toNumber(source.rows),
    sections: toNumber(source.sections),
    green: toNumber(source.green),
    yellow: toNumber(source.yellow),
    red: toNumber(source.red),
    importable: toNumber(source.importable),
    skipped: toNumber(source.skipped),
  };
}

function toIssue(raw: unknown): ImportIssue {
  const source = record(raw);
  return {
    sheet: asText(source.sheet).trim(),
    row: toNumber(source.row),
    level: asText(source.level).trim(),
    message: asText(source.message).trim(),
  };
}

export function toPreview(raw: unknown): ImportPreview {
  const source = record(raw);
  return {
    sheets: array(source.sheets).map(toSheet),
    summary: toSummary(source.summary),
    issues: array(source.issues).map(toIssue),
    normalizations: array(source.normalizations)
      .map((entry) => asText(entry).trim())
      .filter((entry) => entry.length > 0),
    sourceHeaders: toHeaders(source.sourceHeaders),
    correctionIssues: toStringList(source.correctionIssues),
  };
}

export function toOutcome(raw: unknown): ImportOutcome {
  const source = record(raw);
  return {
    createdSheets: toNumber(source.createdSheets),
    createdGroups: toNumber(source.createdGroups),
    createdItems: toNumber(source.createdItems),
    skippedRed: toNumber(source.skippedRed),
    skippedYellow: toNumber(source.skippedYellow),
    skippedExisting: toNumber(source.skippedExisting),
    includeYellow: source.includeYellow === true,
    corrected: toNumber(source.corrected),
    correctionIssues: toStringList(source.correctionIssues),
  };
}

/** One `file` part, which is all the preview wants. */
function fileForm(file: File): FormData {
  const form = new FormData();
  form.append('file', file, file.name);
  return form;
}

/** The fields of a correction, in the order the payload lists them. `label` is the description. */
const CORRECTION_FIELDS = [
  'label',
  'code',
  'subcategory',
  'brand',
  'reference',
  'serial',
  'notes',
  'dimensions',
] as const;

/**
 * The corrections as the API parses them: one JSON array, every property a string, `source` and
 * `row` identifying the line. Trimmed, deduplicated by `source`+`row`, and **with empty fields left
 * out** — an empty string would clear the cell rather than keep what the file says.
 *
 * The attribute map is the one place where an empty string is data: it is how the editor says the
 * attribute is gone, so an empty value is written as `""` and only an empty *name* is dropped.
 */
export function toCorrectionPayload(
  corrections: readonly ImportCorrection[],
): Record<string, unknown>[] {
  const payload: Record<string, unknown>[] = [];
  const seen = new Set<string>();

  for (const correction of corrections) {
    const row = Math.trunc(Number(correction.row));
    if (!Number.isFinite(row) || row < 1) continue;

    const source = (correction.source ?? '').trim();
    // A row number repeats across sheets, so the pair is the identity. Sending the same line twice
    // is not a correction, it is the same correction said again.
    const key = `${source.toLowerCase()}|${row}`;
    if (seen.has(key)) continue;
    seen.add(key);

    const entry: Record<string, unknown> = {};
    if (source) entry.source = source;
    entry.row = row;
    for (const field of CORRECTION_FIELDS) {
      const value = (correction[field] ?? '').trim();
      if (value) entry[field] = value;
    }

    const attributes: Record<string, string> = {};
    for (const [label, value] of Object.entries(correction.attributes ?? {})) {
      const name = label.trim();
      if (!name) continue;
      attributes[name] = (value ?? '').trim();
    }
    if (Object.keys(attributes).length > 0) entry.attributes = attributes;

    payload.push(entry);
  }

  return payload;
}

/** Appends the one `corrections` part both calls take, and nothing at all when there is none. */
function appendCorrections(form: FormData, corrections: readonly ImportCorrection[]): void {
  const payload = toCorrectionPayload(corrections);
  if (payload.length === 0) return;
  form.append('corrections', JSON.stringify(payload));
}

/** The body `POST /api/import/preview` takes: the file, plus every correction made so far. */
export function buildPreviewForm(
  file: File,
  corrections: readonly ImportCorrection[] = [],
): FormData {
  const form = fileForm(file);
  appendCorrections(form, corrections);
  return form;
}

/** The body `POST /api/import` takes: the file, a boolean as text, one part per skipped code, and
 * the corrections, so the import writes what the user fixed instead of what the file says. */
export function buildImportForm(file: File, request: ImportRequest = {}): FormData {
  const form = fileForm(file);
  form.append('includeYellow', request.includeYellow === false ? 'false' : 'true');

  const seen = new Set<string>();
  for (const raw of request.skipCodes ?? []) {
    const code = raw.trim();
    // The API compares codes case-insensitively with a set: sending a duplicate is noise, not data.
    if (!code || seen.has(code.toLowerCase())) continue;
    seen.add(code.toLowerCase());
    form.append('skipCodes', code);
  }

  appendCorrections(form, request.corrections ?? []);
  return form;
}

/**
 * The header aliases the reader uses for the subcategory (see `ItemImporter.HeaderAliases`). The
 * item carries the placed group, not the cell the file wrote, so the editor rereads the raw row.
 */
const SUBCATEGORY_HEADERS = [
  'SUB CATEGORIA',
  'SUB CATEGORÍA',
  'SUBCATEGORIA',
  'CATEGORIA',
  'GRUPO',
] as const;

/** Trim, collapse inner runs of spaces, drop the BOM and uppercase — the reader's own folding. */
function normalizeHeader(value: string): string {
  return value
    .replace(/\uFEFF/g, '')
    .trim()
    .replace(/\s+/g, ' ')
    .toUpperCase();
}

/** The header the reader accepts for the description (see `ItemImporter.HeaderAliases`). */
const DESCRIPTION_HEADERS = [
  'DESCRIPCION',
  'DESCRIPCIÓN',
  'DESCRIPTION',
  'NOMBRE',
  'ELEMENTO',
] as const;

/** The cell a header names, or `undefined` when the row has no such column. */
function rawCell(item: ImportItem, aliases: readonly string[]): string | undefined {
  const raw = item.raw;
  if (!raw) return undefined;
  const header = Object.keys(raw).find((column) => aliases.includes(normalizeHeader(column)));
  return header ? (raw[header] ?? '').trim() : undefined;
}

/**
 * The subcategory cell of the file's row, empty when the row carries no such column. Only the raw
 * row has it: `ImportItem` carries where the row was placed, which is the answer, not the question.
 */
export function subcategoryFromRaw(item: ImportItem): string {
  return rawCell(item, SUBCATEGORY_HEADERS) ?? '';
}

/**
 * The description cell of the file's row, or `null` when the row has no such column.
 *
 * `label` is **not** the description in every case: the API fills it with a display fallback (the
 * code) when a row has none and must still be listed. Editing that as if it were the description
 * would write a code as a name, so the editor reads the file's own cell instead.
 */
export function descriptionFromRaw(item: ImportItem): string | null {
  return rawCell(item, DESCRIPTION_HEADERS) ?? null;
}

export const importService = {
  /** Reads the workbook and returns the tree it describes. Writes nothing. */
  async preview(file: File, corrections: ImportCorrection[] = []): Promise<ImportPreview> {
    return toPreview(
      await api.postForm<unknown>('/import/preview', buildPreviewForm(file, corrections)),
    );
  },

  /** Writes green rows and the yellow ones the caller kept; red rows never go in. */
  async run(file: File, request: ImportRequest = {}): Promise<ImportOutcome> {
    return toOutcome(await api.postForm<unknown>('/import', buildImportForm(file, request)));
  },
};
