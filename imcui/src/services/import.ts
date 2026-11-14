import { api } from './api';
import { asText } from './utilities';

/**
 * The spreadsheet import (see `ImportController` / `ItemImporter`). Two calls, both multipart and
 * both admin:
 *
 *   POST /api/import/preview   field `file`                     — reads the file, writes nothing
 *   POST /api/import           fields `file`, `includeYellow`, `skipCodes` (repeated)
 *
 * The panel shows the preview first and only then decides: red rows never go in, a yellow row goes
 * in unless the user unticks it, and a green row always does.
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
}

export interface ImportGroup {
  name: string;
  status: ImportStatus;
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
}

export interface ImportRequest {
  /** Yellow rows go in as a class. Defaults to true, which is what the API assumes. */
  includeYellow?: boolean;
  /** One entry per yellow code the user unticked. The API keys on codes, not on rows. */
  skipCodes?: string[];
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
  };
}

function toGroup(raw: unknown, sheet: string, used: Set<string>): ImportGroup {
  const source = record(raw);
  return {
    name: asText(source.name).trim(),
    status: toStatus(source.status),
    green: toNumber(source.green),
    yellow: toNumber(source.yellow),
    red: toNumber(source.red),
    items: array(source.items).map((item) => toItem(item, sheet, used)),
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
  };
}

/** One `file` part, which is all the preview wants. */
function fileForm(file: File): FormData {
  const form = new FormData();
  form.append('file', file, file.name);
  return form;
}

/** The body `POST /api/import` takes: the file, a boolean as text, and one part per skipped code. */
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

  return form;
}

export const importService = {
  /** Reads the workbook and returns the tree it describes. Writes nothing. */
  async preview(file: File): Promise<ImportPreview> {
    return toPreview(await api.postForm<unknown>('/import/preview', fileForm(file)));
  },

  /** Writes green rows and the yellow ones the caller kept; red rows never go in. */
  async run(file: File, request: ImportRequest = {}): Promise<ImportOutcome> {
    return toOutcome(await api.postForm<unknown>('/import', buildImportForm(file, request)));
  },
};
