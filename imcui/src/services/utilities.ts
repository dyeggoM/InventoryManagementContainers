export function fileToBase64(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();

    reader.onload = () => {
      const result = reader.result as string;
      resolve(result ?? '');
    };

    reader.onerror = () => {
      reject(reader.error ?? new Error('Failed to read file'));
    };

    reader.readAsDataURL(file);
  });
}


/** `BuyPrice` is a key in the data, but a person reads "Buy Price". */
export function prettyLabel(key: string): string {
  return key
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/^\s+|\s+$/g, '');
}

/**
 * A scalar as text. Anything else (an object, an array, a function) is not a name or a timestamp,
 * and stringifying it would put "[object Object]" on the screen — so it becomes an empty string.
 */
export function asText(value: unknown): string {
  if (typeof value === 'string') return value;
  if (typeof value === 'number' || typeof value === 'boolean') return String(value);
  return '';
}

/**
 * A timestamp for a table cell: the API sends UTC, the reader wants their own clock. An invalid or
 * missing value is shown as a dash rather than "Invalid Date".
 */
export function formatTimestamp(value: string | null | undefined): string {
  if (!value) return '—';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '—';
  return date.toLocaleString();
}

/** A `yyyy-mm-dd` value for a date input, from an ISO timestamp. */
export function toDateInputValue(value: string | null | undefined): string {
  if (!value) return '';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  return date.toISOString().slice(0, 10);
}
