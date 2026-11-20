import { describe, expect, it } from 'vitest';
import { attributeRows, attributesJson, blankRows, readAttributeMap } from './attributes';

/** The codes Configuration defines, in its order. The labels are not in this file on purpose. */
const catalogue = ['Brand', 'Type', 'Notes'];

describe('the attributes round trip', () => {
  it('saves back exactly the keys it read, whatever the labels are called', () => {
    const stored = '{"Brand":"Bosch","Type":"Part"}';

    const { rows, malformed } = attributeRows(stored, catalogue);

    expect(malformed).toBe(false);
    expect(rows).toEqual([
      { code: 'Brand', value: 'Bosch' },
      { code: 'Type', value: 'Part' },
      { code: 'Notes', value: '' },
    ]);
    // The design in one assertion: reading and writing the form does not touch the stored keys.
    expect(attributesJson(rows)).toBe(stored);
  });

  it('keeps a key the catalogue does not define, at the end', () => {
    const stored = '{"Brand":"DW","Serial":"SN-0042"}';

    const { rows } = attributeRows(stored, catalogue);

    expect(rows.map((row) => row.code)).toEqual(['Brand', 'Type', 'Notes', 'Serial']);
    expect(attributesJson(rows)).toBe(stored);
  });

  it('writes the value that is typed, not the label of the value', () => {
    const rows = [
      { code: 'Brand', value: 'Bosch' },
      { code: 'Type', value: 'Part' },
      { code: '', value: 'a row with no name yet' },
    ];

    // An incomplete row is not written: the key is the identity and an empty one identifies nothing.
    expect(attributesJson(rows)).toBe('{"Brand":"Bosch","Type":"Part"}');
  });

  it('survives a value with a quote, a backslash or a newline', () => {
    const rows = [{ code: 'Notes', value: 'line "one"\nline\\two' }];

    expect(JSON.parse(attributesJson(rows))).toEqual({ Notes: 'line "one"\nline\\two' });
  });

  it('reports a stored value that is not a JSON object instead of throwing', () => {
    const broken = attributeRows('{"Brand":', catalogue);

    expect(broken.malformed).toBe(true);
    expect(broken.rows).toEqual(blankRows(catalogue));
    expect(readAttributeMap('{"Brand":')).toBeNull();
    expect(readAttributeMap(null)).toEqual({});
  });
});
