import { api } from './api';
import { asText } from './utilities';

/**
 * The audit contract the dashboard is built against. Like the user routes, `GET /api/audit` does
 * not exist in the API yet: the panel is written to this shape and shows "API pendiente" when the
 * call answers 404.
 *
 *   GET /api/audit?actor=&entity=&action=&from=&to=&page=
 */
export interface AuditEntry {
  id: string | number;
  /** ISO timestamp of the change. */
  occurredAtUtc: string;
  actorEmail: string;
  action: string;
  entity: string;
  entityId?: string | number | null;
  /** The record as it was, and as it was left. Stored as JSON by the API. */
  before?: unknown;
  after?: unknown;
}

export interface AuditQuery {
  actor?: string | null;
  entity?: string | null;
  action?: string | null;
  /** ISO dates (yyyy-mm-dd) from the date inputs. */
  from?: string | null;
  to?: string | null;
  page?: number;
  pageSize?: number;
}

export interface AuditPage {
  items: AuditEntry[];
  total: number;
  page: number;
  pageSize: number;
}

/** Only the filled-in fields go out: an empty filter must not narrow the result to nothing. */
export function buildAuditQuery(query: AuditQuery): string {
  const params = new URLSearchParams();
  const assign = (key: string, value: string | number | null | undefined) => {
    if (value === null || value === undefined) return;
    const text = String(value).trim();
    if (text) params.set(key, text);
  };

  assign('actor', query.actor);
  assign('entity', query.entity);
  assign('action', query.action);
  assign('from', query.from);
  assign('to', query.to);
  if (query.page !== undefined) assign('page', query.page);
  if (query.pageSize !== undefined) assign('pageSize', query.pageSize);

  const text = params.toString();
  return text ? `?${text}` : '';
}

/** A claim or a row value as text: anything that is not a scalar is not a name, and is dropped. */
function toEntry(raw: Record<string, unknown>): AuditEntry {
  const id = raw.id;
  const entityId = raw.entityId;
  return {
    id: typeof id === 'string' || typeof id === 'number' ? id : '',
    occurredAtUtc: asText(raw.occurredAtUtc) || asText(raw.timestampUtc),
    actorEmail: asText(raw.actorEmail) || asText(raw.actor),
    action: asText(raw.action),
    entity: asText(raw.entity),
    entityId:
      typeof entityId === 'string' || typeof entityId === 'number' ? entityId : null,
    before: raw.before ?? null,
    after: raw.after ?? null,
  };
}

export const auditService = {
  async search(query: AuditQuery = {}): Promise<AuditPage> {
    const page = query.page ?? 1;
    const pageSize = query.pageSize ?? 25;
    const response = await api.get<AuditPage | AuditEntry[]>(`/audit${buildAuditQuery(query)}`);
    if (Array.isArray(response)) {
      return {
        items: response.map((row) => toEntry(row as unknown as Record<string, unknown>)),
        total: response.length,
        page,
        pageSize,
      };
    }
    const items = (response?.items ?? []).map((row) =>
      toEntry(row as unknown as Record<string, unknown>),
    );
    return {
      items,
      total: response?.total ?? items.length,
      page: response?.page ?? page,
      pageSize: response?.pageSize ?? pageSize,
    };
  },
};

function csvCell(value: unknown): string {
  if (value === null || value === undefined) return '';
  const text = typeof value === 'string' ? value : JSON.stringify(value);
  // A quote, a comma or a newline all break a naive CSV: quote everything and double the quotes.
  return `"${(text ?? '').replace(/"/g, '""')}"`;
}

/** The rows as a spreadsheet: RFC 4180 quoting, one row per change, before/after as JSON. */
export function auditToCsv(entries: readonly AuditEntry[]): string {
  const header = ['id', 'occurredAtUtc', 'actorEmail', 'action', 'entity', 'entityId', 'before', 'after'];
  const lines = [header.join(',')];
  for (const entry of entries) {
    lines.push(
      [
        csvCell(entry.id),
        csvCell(entry.occurredAtUtc),
        csvCell(entry.actorEmail),
        csvCell(entry.action),
        csvCell(entry.entity),
        csvCell(entry.entityId),
        csvCell(entry.before),
        csvCell(entry.after),
      ].join(','),
    );
  }
  return lines.join('\r\n');
}

/** Pretty JSON for the before/after viewer; anything unparseable is shown as it came. */
export function formatAuditPayload(value: unknown): string {
  if (value === null || value === undefined) return '—';
  if (typeof value === 'string') {
    try {
      return JSON.stringify(JSON.parse(value), null, 2);
    } catch {
      return value;
    }
  }
  try {
    return JSON.stringify(value, null, 2) ?? '[unserializable]';
  } catch {
    return '[unserializable]';
  }
}
