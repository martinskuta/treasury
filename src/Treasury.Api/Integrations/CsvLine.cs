using System.Text;

namespace Treasury.Api.Integrations;

/// Splitting one CSV line into fields, shared by the CSV readers.
internal static class CsvLine
{
    /// RFC 4180-style fields of one line (a quoted field may not span lines).
    public static List<string> Split(string line, char separator, int lineNumber)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else current.Append(c);
            }
            else if (c == '"' && current.ToString().Trim().Length == 0) { current.Clear(); quoted = true; }
            else if (c == separator) { fields.Add(current.ToString()); current.Clear(); }
            else current.Append(c);
        }
        if (quoted) throw new StatementFormatException($"line {lineNumber}: a quoted value is not closed.");
        fields.Add(current.ToString());
        return fields;
    }
}
