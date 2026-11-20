using System.Text;

namespace JobHuntHQ.Services;

/// <summary>
/// A small hand-written CSV reader (RFC 4180-ish): handles quoted fields,
/// commas and newlines inside quotes, doubled quotes, and CRLF or LF endings.
/// Good enough for tracker exports without taking a package dependency.
/// </summary>
public static class CsvParser
{
    /// <summary>One parsed record: its fields and the 1-based line it started on.</summary>
    public sealed class CsvRecord
    {
        public required int LineNumber { get; init; }
        public required List<string> Fields { get; init; }
    }

    public static List<CsvRecord> Parse(string text)
    {
        var records = new List<CsvRecord>();
        var field = new StringBuilder();
        var fields = new List<string>();
        var inQuotes = false;
        var recordHasContent = false;
        var line = 1;
        var recordStartLine = 1;

        void EndField()
        {
            fields.Add(field.ToString());
            field.Clear();
        }

        void EndRecord()
        {
            EndField();
            // Skip completely blank lines.
            if (recordHasContent || fields.Any(f => f.Length > 0))
            {
                records.Add(new CsvRecord { LineNumber = recordStartLine, Fields = new List<string>(fields) });
            }
            fields.Clear();
            recordHasContent = false;
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    // Doubled quote inside a quoted field = a literal quote.
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    if (c == '\n')
                    {
                        line++;
                    }
                    field.Append(c);
                }
                continue;
            }

            switch (c)
            {
                case '"':
                    if (field.Length == 0)
                    {
                        inQuotes = true;
                        recordHasContent = true;
                    }
                    else
                    {
                        field.Append(c);
                    }
                    break;
                case ',':
                    EndField();
                    recordHasContent = true;
                    break;
                case '\r':
                    // Treat CRLF as one line ending; a lone CR also ends a record.
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }
                    EndRecord();
                    line++;
                    recordStartLine = line;
                    break;
                case '\n':
                    EndRecord();
                    line++;
                    recordStartLine = line;
                    break;
                default:
                    field.Append(c);
                    recordHasContent = true;
                    break;
            }
        }

        // Flush a final record that has no trailing newline.
        if (recordHasContent || fields.Count > 0 || field.Length > 0)
        {
            EndRecord();
        }

        return records;
    }
}
