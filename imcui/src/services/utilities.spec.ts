import { describe, expect, it } from 'vitest';
import { prettyLabel } from './utilities';

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
