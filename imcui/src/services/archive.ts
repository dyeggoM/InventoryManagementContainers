import { api } from './api';
import { asText } from './utilities';

/**
 * The archive contract the dashboard is built against. Deleting an item is archival: the row moves
 * to `ItemsArchive` with its attachments, and the API can list it back and restore it.
 *
 *   GET   /api/item/archive?page=&pageSize=&from=&to=&actor=&label=   (superadmin)
 *   PATCH /api/item/{id}/activate                                     (admin and up)
 */
export interface ArchiveEntry {
  archiveId: number;
  /** The item's original id: the one `/activate` takes. */
  itemId: number;
  label: string;
  icon: string;
  parentId: number | null;
  attributes: string | null;
  isActive: boolean;
  createdAt: string | null;
  createdBy: string | null;
  updatedAt: string | null;
  updatedBy: string | null;
  /** ISO timestamp of the deletion that produced this snapshot. */
  deletedAtUtc: string | null;
  deletedById: string | null;
  deletedBy: string | null;
  /** The attachment rows that were removed with the item, as JSON; null when there were none. */
  attachmentsJson: string | null;
  restoredAtUtc: string | null;
  restoredById: string | null;
}

export interface ArchiveQuery {
  page?: number;
  pageSize?: number;
  /** ISO dates (yyyy-mm-dd) from the date inputs. */
  from?: string | null;
  to?: string | null;
  actor?: string | null;
  label?: string | null;
}

export interface ArchivePage {
  items: ArchiveEntry[];
  page: number;
  pageSize: number;
  total: number;
}

/** The item the API answers `/activate` with: the row it put back in the tree. */
export interface RestoredItem {
  id: number;
  label: string;
  icon?: string;
  parentId?: number | null;
}

/** Only the filled-in filters go out: an empty filter must not narrow the result to nothing. */
export function buildArchiveQuery(query: ArchiveQuery): string {
  const params = new URLSearchParams();
  const assign = (key: string, value: string | number | null | undefined) => {
    if (value === null || value === undefined) return;
    const text = String(value).trim();
    if (text) params.set(key, text);
  };

  assign('page', query.page);
  assign('pageSize', query.pageSize);
  assign('from', query.from);
  assign('to', query.to);
  assign('actor', query.actor);
  assign('label', query.label);

  const text = params.toString();
  return text ? `?${text}` : '';
}

/** A row as the table expects it: missing scalars become null, a wrong type is not trusted. */
function toEntry(raw: Record<string, unknown>): ArchiveEntry {
  const number = (value: unknown): number =>
    typeof value === 'number' && Number.isFinite(value) ? value : Number(asText(value)) || 0;
  const text = (value: unknown): string | null => {
    const result = asText(value);
    return result ? result : null;
  };

  return {
    archiveId: number(raw.archiveId),
    itemId: number(raw.itemId),
    label: asText(raw.label),
    icon: asText(raw.icon),
    parentId: typeof raw.parentId === 'number' ? raw.parentId : null,
    attributes: text(raw.attributes),
    isActive: raw.isActive === true,
    createdAt: text(raw.createdAt),
    createdBy: text(raw.createdBy),
    updatedAt: text(raw.updatedAt),
    updatedBy: text(raw.updatedBy),
    deletedAtUtc: text(raw.deletedAtUtc),
    deletedById: text(raw.deletedById),
    deletedBy: text(raw.deletedBy),
    attachmentsJson: text(raw.attachmentsJson),
    restoredAtUtc: text(raw.restoredAtUtc),
    restoredById: text(raw.restoredById),
  };
}

export const archiveService = {
  async list(query: ArchiveQuery = {}): Promise<ArchivePage> {
    const page = query.page ?? 1;
    const pageSize = query.pageSize ?? 25;
    const response = await api.get<ArchivePage>(
      `/item/archive${buildArchiveQuery({ ...query, page, pageSize })}`,
    );
    const items = (response?.items ?? []).map((row) =>
      toEntry(row as unknown as Record<string, unknown>),
    );
    return {
      items,
      page: response?.page ?? page,
      pageSize: response?.pageSize ?? pageSize,
      total: response?.total ?? items.length,
    };
  },

  /**
   * Puts an archived item back. A 404 means there is no unrestored snapshot left for that id, so
   * the caller can say "already restored" and reload instead of treating it as a failure.
   */
  restore(itemId: number): Promise<RestoredItem> {
    return api.patch<RestoredItem>(`/item/${itemId}/activate`);
  },
};
