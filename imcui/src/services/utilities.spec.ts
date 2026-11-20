import { describe, expect, it } from 'vitest';
import { formatTimestamp, prettyLabel } from './utilities';

describe('prettyLabel', () => {
  it('reads a camelCase key as words', () => {
    expect(prettyLabel('BuyPrice')).toBe('Buy Price');
    expect(prettyLabel('RentPrice')).toBe('Rent Price');
    expect(prettyLabel('SerialNumber')).toBe('Serial Number');
  });

  it('leaves a plain key and a hand-typed one alone', () => {
    expect(prettyLabel('Brand')).toBe('Brand');
    expect(prettyLabel('Serial')).toBe('Serial');
    expect(prettyLabel('my own key')).toBe('my own key');
  });

  it('splits at the capital that follows a digit', () => {
    expect(prettyLabel('Model2Number')).toBe('Model2 Number');
  });
});

describe('formatTimestamp', () => {
  it('writes the date day first, on a 24-hour clock, with every field two digits wide', () => {
    // The shape, not the hour: the value is the reader's own clock, so only the order, the clock
    // and the width are fixed. A browser left in en-US rendered this as «11/10/2026, 12:30:00 PM»,
    // and es-ES without explicit fields left the hour unpadded («7:30:00»).
    expect(formatTimestamp('2026-11-10T12:30:00Z')).toMatch(
      /^\d{2}\/\d{2}\/\d{4}, \d{2}:\d{2}:\d{2}$/,
    );
    expect(formatTimestamp('2026-11-10T12:30:00Z')).not.toMatch(/AM|PM/);
  });

  it('shows a dash for a missing or unreadable value', () => {
    expect(formatTimestamp(null)).toBe('—');
    expect(formatTimestamp('')).toBe('—');
    expect(formatTimestamp('not a date')).toBe('—');
  });
});
