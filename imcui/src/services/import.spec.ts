import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from './api';
import { buildImportForm, importService, toOutcome, toPreview, type ImportPreview } from './import';

function jsonResponse(body: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    statusText: String(status),
    json: () => Promise.resolve(body),
  } as unknown as Response;
}

/** Stubs fetch and hands back every call, so the multipart body can be read part by part. */
function captureFetch(body: unknown = {}, status = 200) {
  const calls: { url: string; init?: RequestInit | undefined }[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn((url: string, init?: RequestInit) => {
      calls.push({ url, init });
      return Promise.resolve(jsonResponse(body, status));
    }),
  );
  return calls;
}

function form(call: { init?: RequestInit | undefined }): FormData {
  const body = call.init?.body;
  if (!(body instanceof FormData)) throw new Error('the request body is not a FormData');
  return body;
}

function workbook(): File {
  return new File(['dummy'], 'stock.xlsx', {
    type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  });
}

/** The shape the API answers with, trimmed to one sheet, one group and three rows. */
function previewAnswer(): Record<string, unknown> {
  return {
    sheets: [
      {
        name: 'BACKLINE',
        status: 'yellow',
        green: 1,
        yellow: 1,
        red: 1,
        skippedSections: 2,
        groups: [
          {
            name: 'BASE TIPO BOOM',
            status: 'yellow',
            green: 1,
            yellow: 1,
            red: 1,
            items: [
              {
                label: 'Base de brazo DW 9000',
                code: '-H0001',
                brand: 'DW',
                reference: '9700',
                status: 'green',
                reasons: [],
                row: 12,
              },
              {
                label: 'Cuerpo de bombo',
                code: '-H0002',
                status: 'yellow',
                reasons: ['Sin dimensiones declaradas'],
                row: 13,
              },
              // A red row: no code, no brand, and a reason. Everything nullable is absent.
              {
                label: 'Soporte roto',
                status: 'red',
                reasons: ['Código duplicado en la hoja'],
                row: 14,
              },
            ],
          },
        ],
      },
    ],
    summary: {
      rows: 3,
      sections: 2,
      green: 1,
      yellow: 1,
      red: 1,
      importable: 2,
      skipped: 1,
    },
    issues: [{ sheet: 'BACKLINE', row: 14, level: 'red', message: 'Código duplicado' }],
    normalizations: ['«GTAR COMBO» → «GTR COMBO»'],
  };
}

beforeEach(() => {
  localStorage.clear();
  vi.unstubAllGlobals();
});

describe('importService.preview', () => {
  it('POSTs the file as multipart to /import/preview and maps the tree', async () => {
    const calls = captureFetch(previewAnswer());

    const preview = await importService.preview(workbook());

    expect(calls).toHaveLength(1);
    expect(calls[0]!.url).toBe('http://localhost:5173/api/import/preview');
    expect(calls[0]!.init?.method).toBe('POST');

    const sent = form(calls[0]!);
    expect(sent.get('file')).toBeInstanceOf(File);
    expect((sent.get('file') as File).name).toBe('stock.xlsx');

    expect(preview.summary).toEqual({
      rows: 3,
      sections: 2,
      green: 1,
      yellow: 1,
      red: 1,
      importable: 2,
      skipped: 1,
    });
    expect(preview.normalizations).toEqual(['«GTAR COMBO» → «GTR COMBO»']);
    expect(preview.issues).toEqual([
      { sheet: 'BACKLINE', row: 14, level: 'red', message: 'Código duplicado' },
    ]);

    const sheet = preview.sheets[0]!;
    expect(sheet).toMatchObject({ name: 'BACKLINE', status: 'yellow', skippedSections: 2 });

    const item = sheet.groups[0]!.items[0]!;
    expect(item).toEqual({
      key: 'BACKLINE#12',
      label: 'Base de brazo DW 9000',
      code: '-H0001',
      brand: 'DW',
      reference: '9700',
      serial: null,
      notes: null,
      dimensions: null,
      status: 'green',
      reasons: [],
      row: 12,
    });
  });

  it('reads a nullable field the API omitted as null, never as undefined', async () => {
    captureFetch(previewAnswer());

    const preview = await importService.preview(workbook());
    const red = preview.sheets[0]!.groups[0]!.items[2]!;

    expect(red.code).toBeNull();
    expect(red.brand).toBeNull();
    expect(red.reasons).toEqual(['Código duplicado en la hoja']);
  });

  it('gives every row a key of its own, even when two rows claim the same line', () => {
    const preview: ImportPreview = toPreview({
      sheets: [
        {
          name: 'BACKLINE',
          groups: [
            {
              name: 'A',
              items: [
                { label: 'uno', row: 12, status: 'green' },
                { label: 'dos', row: 12, status: 'green' },
              ],
            },
            { name: 'B', items: [{ label: 'tres', row: 12, status: 'green' }] },
          ],
        },
      ],
    });

    const keys = preview.sheets[0]!.groups.flatMap((group) => group.items.map((item) => item.key));

    expect(new Set(keys).size).toBe(3);
    expect(keys[0]).toBe('BACKLINE#12');
    expect(keys[1]).toBe('BACKLINE#12~2');
  });

  it('reads a colour it does not know as red, which is the one that is never imported', () => {
    const preview = toPreview({
      sheets: [{ name: 'HOJA', groups: [{ name: 'G', items: [{ label: 'x', status: 'azul' }] }] }],
    });

    expect(preview.sheets[0]!.groups[0]!.items[0]!.status).toBe('red');
  });

  it('survives an answer that is not the envelope at all', () => {
    const preview = toPreview(null);

    expect(preview).toEqual({
      sheets: [],
      summary: { rows: 0, sections: 0, green: 0, yellow: 0, red: 0, importable: 0, skipped: 0 },
      issues: [],
      normalizations: [],
    });
  });

  it('maps a 400 problem-details answer to the API message', async () => {
    captureFetch({ title: 'Unreadable spreadsheet', detail: 'The file could not be read.' }, 400);

    const error = await importService.preview(workbook()).catch((caught: unknown) => caught);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(400);
    expect((error as ApiError).message).toBe('The file could not be read.');
  });
});

describe('buildImportForm', () => {
  it('sends the file, includeYellow as the string the API parses, and no skipCodes by default', () => {
    const sent = buildImportForm(workbook());

    expect((sent.get('file') as File).name).toBe('stock.xlsx');
    expect(sent.get('includeYellow')).toBe('true');
    expect(sent.getAll('skipCodes')).toEqual([]);
  });

  it('sends includeYellow "false" and repeats skipCodes once per unticked code', () => {
    const sent = buildImportForm(workbook(), {
      includeYellow: false,
      skipCodes: ['-H0002', '-H0003'],
    });

    expect(sent.get('includeYellow')).toBe('false');
    expect(sent.getAll('skipCodes')).toEqual(['-H0002', '-H0003']);
  });

  it('drops an empty code and a code sent twice in another case', () => {
    const sent = buildImportForm(workbook(), {
      skipCodes: ['-H0002', '   ', '-h0002', ' -H0004 '],
    });

    expect(sent.getAll('skipCodes')).toEqual(['-H0002', '-H0004']);
  });
});

describe('importService.run', () => {
  it('POSTs the multipart body to /import and maps what it wrote', async () => {
    const calls = captureFetch({
      createdSheets: 1,
      createdGroups: 4,
      createdItems: 455,
      skippedRed: 105,
      skippedYellow: 0,
      skippedExisting: 7,
      includeYellow: true,
    });

    const outcome = await importService.run(workbook(), { skipCodes: ['-H0002'] });

    expect(calls[0]!.url).toBe('http://localhost:5173/api/import');
    expect(calls[0]!.init?.method).toBe('POST');

    const sent = form(calls[0]!);
    expect(sent.get('includeYellow')).toBe('true');
    expect(sent.getAll('skipCodes')).toEqual(['-H0002']);

    expect(outcome).toEqual({
      createdSheets: 1,
      createdGroups: 4,
      createdItems: 455,
      skippedRed: 105,
      skippedYellow: 0,
      skippedExisting: 7,
      includeYellow: true,
    });
  });

  it('keeps includeYellow false when the whole class was left out', async () => {
    captureFetch({
      createdSheets: 1,
      createdGroups: 2,
      createdItems: 350,
      skippedRed: 105,
      skippedYellow: 105,
      skippedExisting: 0,
      includeYellow: false,
    });

    const outcome = await importService.run(workbook(), { includeYellow: false });

    expect(outcome.includeYellow).toBe(false);
    expect(outcome.skippedYellow).toBe(105);
  });

  it('does not trust a missing count: an absent number reads as zero', () => {
    expect(toOutcome({ createdItems: '12' })).toEqual({
      createdSheets: 0,
      createdGroups: 0,
      createdItems: 12,
      skippedRed: 0,
      skippedYellow: 0,
      skippedExisting: 0,
      includeYellow: false,
    });
  });
});
