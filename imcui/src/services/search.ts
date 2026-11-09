import type { TreeEntry } from '../composables/useTreeEditor';

/** The attribute that made a record match, shown under its name. */
export interface MatchedPair {
  label: string;
  value: string;
}

/** Keys are compared with punctuation removed, so "buy price" finds BuyPrice. */
function normalizeKey(value: string): string {
  return value.toLowerCase().replace(/[^a-z0-9]/g, '');
}

/**
 * The first attribute whose key or value contains the needle. Values match as plain substrings; a
 * key matches with punctuation and spacing ignored, because a person types "buy price" for BuyPrice.
 */
export function attributeMatch(node: TreeEntry, needle: string): MatchedPair | null {
  if (!node.attributes) return null;

  let parsed: unknown;
  try {
    parsed = JSON.parse(node.attributes);
  } catch {
    // A malformed row simply does not match by attribute; the label search still works.
    return null;
  }

  if (!parsed || typeof parsed !== 'object') return null;

  const needleKey = normalizeKey(needle);
  const needleValue = needle.toLowerCase();
  for (const [key, raw] of Object.entries(parsed as Record<string, unknown>)) {
    // Only scalars are searchable: an object would stringify to "[object Object]".
    const value =
      typeof raw === 'string'
        ? raw
        : typeof raw === 'number' || typeof raw === 'boolean'
          ? String(raw)
          : '';

    const keyHit = needleKey.length > 0 && normalizeKey(key).includes(needleKey);
    if (keyHit || value.toLowerCase().includes(needleValue)) {
      return { label: key, value };
    }
  }

  return null;
}

/**
 * Keeps the records that match and the parents needed to reach them. A node that only survives
 * because a child matched is flagged `hit: false`, so the count does not inflate.
 */
export function filterTree(nodes: TreeEntry[], needle: string): TreeEntry[] {
  const result: TreeEntry[] = [];
  const needleValue = needle.toLowerCase();

  for (const node of nodes) {
    const children = filterTree(node.children ?? [], needle);
    const byLabel = node.label.toLowerCase().includes(needleValue);
    const match = byLabel ? null : attributeMatch(node, needle);

    if (byLabel || children.length > 0 || match) {
      result.push({ ...node, children, match, hit: byLabel || match !== null });
    }
  }

  return result;
}

/** Only rows that matched themselves: a parent shown for context is not a result. */
export function countMatches(nodes: TreeEntry[]): number {
  return nodes.reduce(
    (total, node) => total + (node.hit ? 1 : 0) + countMatches(node.children ?? []),
    0,
  );
}

/** A long value would push the row wide; the name is what matters, the value is a hint. */
export function shortValue(value: string, max = 34): string {
  const text = value.replace(/\s+/g, ' ').trim();
  return text.length > max ? `${text.slice(0, max - 1)}…` : text;
}
