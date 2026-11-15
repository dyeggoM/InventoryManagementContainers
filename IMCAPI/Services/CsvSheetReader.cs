using System.Text;

namespace IMCAPI.Services;

/// <summary>
/// Reads a CSV file into rows of raw text, so the importer can judge them exactly as it judges a
/// workbook: same header aliases, same verdicts, same writer. The only thing this class decides is
/// where a field starts and ends.
///
/// Two things a workshop export gets wrong often enough to handle explicitly: the delimiter (Excel in
/// a Spanish locale writes `;`, so a file whose header says MARCA;REFERENCIA must not arrive as one
/// column) and the encoding (the BOM Excel puts in front of "CSV UTF-8" is not part of the first cell).
/// </summary>
internal static class CsvSheetReader
{
    /// <summary>In preference order, so a tie falls back to the most common export.</summary>
    private static readonly char[] Delimiters = { ',', ';', '\t' };

    /// <summary>How far into the file the delimiter is guessed from; a header sits near the top.</summary>
    private const int DetectionRecords = 64;

    /// <summary>
    /// The file as records of fields, RFC 4180 quoted: `"a, b"` is one field, `""` inside quotes is a
    /// literal quote, and a quoted field may run over a newline. A final record without a trailing
    /// newline is still a record.
    /// </summary>
    public static List<IReadOnlyList<string>> Read(Stream stream)
    {
        var text = Decode(stream);
        return Parse(text, DetectDelimiter(text));
    }

    /// <summary>UTF-8, BOM or not; the BOM is transport, not the first cell's first character.</summary>
    private static string Decode(Stream stream)
    {
        using var reader = new StreamReader(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
        var text = reader.ReadToEnd();
        return text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
    }

    /// <summary>
    /// Counts each candidate outside quotes over the first records and keeps the one that dominates.
    /// The header is not necessarily the first record (some exports carry a title row above it), so
    /// this scans the first logical records instead of one line: every occurrence is still counted
    /// outside quotes, which is what stops `"Nota con coma, y punto y coma"` from choosing a comma.
    /// </summary>
    private static char DetectDelimiter(string text)
    {
        var counts = new Dictionary<char, int>();
        foreach (var delimiter in Delimiters) counts[delimiter] = 0;

        var inQuotes = false;
        var atFieldStart = true;
        var records = 0;

        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];

            if (inQuotes)
            {
                if (current != '"') continue;
                if (index + 1 < text.Length && text[index + 1] == '"') { index++; continue; }
                inQuotes = false;
                continue;
            }

            if (current == '"' && atFieldStart)
            {
                inQuotes = true;
                atFieldStart = false;
                continue;
            }

            if (current == '\r' || current == '\n')
            {
                if (current == '\r' && index + 1 < text.Length && text[index + 1] == '\n') index++;
                records++;
                atFieldStart = true;
                if (records >= DetectionRecords) break;
                continue;
            }

            if (counts.ContainsKey(current))
            {
                counts[current]++;
                atFieldStart = true;
                continue;
            }

            atFieldStart = false;
        }

        var best = Delimiters[0];
        foreach (var delimiter in Delimiters)
        {
            if (counts[delimiter] > counts[best]) best = delimiter;
        }

        // A file with none of them is one column wide, and the comma is as good a choice as any.
        return counts[best] == 0 ? ',' : best;
    }

    private static List<IReadOnlyList<string>> Parse(string text, char delimiter)
    {
        var records = new List<IReadOnlyList<string>>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var atFieldStart = true;

        // `fields` is captured, so a record end can hand the list over and start a fresh one.
        void EndField()
        {
            fields.Add(field.ToString());
            field.Clear();
            atFieldStart = true;
        }

        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];

            if (inQuotes)
            {
                if (current != '"') { field.Append(current); continue; }
                if (index + 1 < text.Length && text[index + 1] == '"') { field.Append('"'); index++; continue; }
                inQuotes = false;
                continue;
            }

            // A quote opens a quoted field only where a field starts; anywhere else it is a literal,
            // which is what keeps an unquoted 14" readable.
            if (current == '"' && atFieldStart)
            {
                inQuotes = true;
                atFieldStart = false;
                continue;
            }

            if (current == delimiter)
            {
                EndField();
                continue;
            }

            if (current == '\r' || current == '\n')
            {
                if (current == '\r' && index + 1 < text.Length && text[index + 1] == '\n') index++;
                EndField();
                records.Add(fields);
                fields = new List<string>();
                continue;
            }

            field.Append(current);
            atFieldStart = false;
        }

        if (fields.Count > 0 || field.Length > 0)
        {
            EndField();
            records.Add(fields);
        }

        return records;
    }
}
