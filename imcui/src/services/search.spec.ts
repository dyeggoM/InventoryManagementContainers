import { describe, expect, it } from 'vitest';
import { attributeMatch, countMatches, filterTree, shortValue } from './search';
import type { TreeEntry } from '../composables/useTreeEditor';

function node(overrides: Partial<TreeEntry> & { id: number; label: string }): TreeEntry {
  return { icon: '', children: [], ...overrides };
}

const kit = node({
  id: 1,
  label: "DW Collector's Series",
  attributes: JSON.stringify({ Brand: 'DW', BuyPrice: '10000000', Serial: 'SN-0042' }),
  children: [
    node({ id: 2, label: 'Kick 22"', attributes: JSON.stringify({ BuyPrice: '5000000' }) }),
    node({ id: 3, label: 'Snare 14"', attributes: JSON.stringify({ Serial: 'SN-0042' }) }),
  ],
});

describe('attributeMatch', () => {
  it('finds a value and reports the pair', () => {
    expect(attributeMatch(kit, 'sn-0042')).toEqual({ label: 'Serial', value: 'SN-0042' });
  });

  it('matches a key with spacing and punctuation ignored', () => {
    expect(attributeMatch(kit, 'buy price')).toEqual({ label: 'BuyPrice', value: '10000000' });
  });

  it('does not care about case on either side', () => {
    expect(attributeMatch(kit, 'brand')).toEqual({ label: 'Brand', value: 'DW' });
  });

  it('returns null for a record with malformed attributes instead of throwing', () => {
    const broken = node({ id: 9, label: 'Broken', attributes: '{"Brand":' });
    expect(attributeMatch(broken, 'brand')).toBeNull();
  });

  it('ignores records with no attributes at all', () => {
    expect(attributeMatch(node({ id: 10, label: 'Empty' }), 'brand')).toBeNull();
  });

  it('does not stringify objects into a match', () => {
    const nested = node({ id: 11, label: 'Nested', attributes: '{"Meta":{"a":1}}' });
    expect(attributeMatch(nested, 'object')).toBeNull();
  });
});

describe('filterTree', () => {
  it('keeps the parents of a matching child, flagged as context only', () => {
    const result = filterTree([kit], 'kick');

    expect(result).toHaveLength(1);
    expect(result[0]!.hit).toBe(false);
    expect(result[0]!.children).toHaveLength(1);
    expect(result[0]!.children[0]!.label).toBe('Kick 22"');
    expect(result[0]!.children[0]!.hit).toBe(true);
  });

  it('marks a record that matched on a value and carries the pair', () => {
    const result = filterTree([kit], 'SN-0042');

    expect(result[0]!.match).toEqual({ label: 'Serial', value: 'SN-0042' });
    expect(result[0]!.hit).toBe(true);
    expect(result[0]!.children).toHaveLength(1);
    expect(result[0]!.children[0]!.label).toBe('Snare 14"');
  });

  it('drops everything when nothing matches', () => {
    expect(filterTree([kit], 'zzz')).toEqual([]);
  });

  it('does not flag a record matched by name with an attribute pair', () => {
    const result = filterTree([kit], 'dw collector');
    expect(result[0]!.match).toBeNull();
    expect(result[0]!.hit).toBe(true);
  });
});

describe('countMatches', () => {
  it('counts records that matched, not the parents kept for context', () => {
    expect(countMatches(filterTree([kit], 'kick'))).toBe(1);
    expect(countMatches(filterTree([kit], 'buy price'))).toBe(2); // the kit and its kick both have it
    expect(countMatches(filterTree([kit], 'brand'))).toBe(1); // only the kit has it
    expect(countMatches(filterTree([kit], 'sn-0042'))).toBe(2);
  });
});

describe('shortValue', () => {
  it('leaves a short value alone and collapses whitespace', () => {
    expect(shortValue('SN-0042')).toBe('SN-0042');
    expect(shortValue('a\n  b')).toBe('a b');
  });

  it('truncates a long value with an ellipsis', () => {
    const long = 'x'.repeat(60);
    const result = shortValue(long);

    expect(result).toHaveLength(34);
    expect(result.endsWith('…')).toBe(true);
  });
});
