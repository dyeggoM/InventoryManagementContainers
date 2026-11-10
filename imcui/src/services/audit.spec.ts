import { describe, expect, it } from 'vitest';
import { auditToCsv, buildAuditQuery, formatAuditPayload, type AuditEntry } from './audit';

const entry: AuditEntry = {
  id: 7,
  occurredAtUtc: '2026-11-10T12:30:00Z',
  actorEmail: 'root@imc.test',
  action: 'Update',
  entity: 'Item',
  entityId: 3,
  before: { label: 'Old, name' },
  after: { label: 'New "name"' },
};

describe('audit query string', () => {
  it('leaves empty filters out', () => {
    expect(buildAuditQuery({})).toBe('');
    expect(buildAuditQuery({ actor: '  ', entity: null })).toBe('');
  });

  it('encodes every filled filter, in a stable order', () => {
    expect(
      buildAuditQuery({
        actor: 'root@imc.test',
        entity: 'Item',
        action: 'Update',
        from: '2026-11-01',
        to: '2026-11-10',
        page: 2,
      }),
    ).toBe(
      '?actor=root%40imc.test&entity=Item&action=Update&from=2026-11-01&to=2026-11-10&page=2',
    );
  });
});

describe('audit CSV', () => {
  it('writes a header even with nothing to export', () => {
    const csv = auditToCsv([]);
    expect(csv.split('\r\n')).toHaveLength(1);
    expect(csv).toContain('occurredAtUtc');
  });

  it('quotes values and doubles the quotes inside them', () => {
    const csv = auditToCsv([{ ...entry, before: 'say "hi"', after: { label: 'plain' } }]);
    const row = csv.split('\r\n')[1] ?? '';

    expect(csv.split('\r\n')).toHaveLength(2);
    // A comma or a quote must not shift a column.
    expect(row).toContain('"say ""hi"""');
    expect(row.split('","')).toHaveLength(8);
  });

  it('keeps a comma inside a quoted cell instead of splitting the column', () => {
    const csv = auditToCsv([entry]);
    const row = csv.split('\r\n')[1] ?? '';

    // before is JSON containing a comma: it must stay inside one quoted cell.
    expect(row).toContain('""Old, name""');
    expect(row.split('","')).toHaveLength(8);
  });
});

describe('before/after viewer', () => {
  it('pretty-prints JSON, including JSON stored as a string', () => {
    expect(formatAuditPayload({ label: 'A' })).toBe('{\n  "label": "A"\n}');
    expect(formatAuditPayload('{"label":"A"}')).toBe('{\n  "label": "A"\n}');
  });

  it('shows a dash for a missing payload and the raw text for unparseable JSON', () => {
    expect(formatAuditPayload(null)).toBe('—');
    expect(formatAuditPayload('not json')).toBe('not json');
  });
});
