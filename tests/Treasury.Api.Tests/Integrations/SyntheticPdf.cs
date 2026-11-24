using System.Globalization;
using System.Text;

namespace Treasury.Api.Tests.Integrations;

/// A piece of text on a page of a synthetic PDF: its left edge, baseline, font size and value.
internal sealed record PdfText(double X, double Y, double Size, string Value);

/// Just enough of a PDF writer for the synthetic statements: A4 pages of text in Helvetica (WinAnsi encoding, so
/// "€", "£", "¥" and accented Latin letters work).
internal static class SyntheticPdf
{
    public static byte[] Write(List<List<PdfText>> pages)
    {
        var objects = new List<byte[]>
        {
            Ascii("<< /Type /Catalog /Pages 2 0 R >>"),
            Ascii($"<< /Type /Pages /Count {pages.Count} /Kids [{string.Join(' ', pages.Select((_, i) => $"{4 + 2 * i} 0 R"))}] >>"),
            Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"),
        };
        for (var i = 0; i < pages.Count; i++)
        {
            objects.Add(Ascii($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595.28 841.88] " +
                              $"/Resources << /Font << /F1 3 0 R >> >> /Contents {5 + 2 * i} 0 R >>"));
            var content = new MemoryStream();
            foreach (var text in pages[i])
            {
                content.Write(Ascii(string.Create(CultureInfo.InvariantCulture,
                    $"BT /F1 {text.Size} Tf {text.X:0.##} {text.Y:0.##} Td (")));
                foreach (var c in text.Value)
                {
                    if (c is '(' or ')' or '\\') content.WriteByte((byte)'\\');
                    content.WriteByte(WinAnsi(c));
                }
                content.Write(Ascii(") Tj ET\n"));
            }
            objects.Add([.. Ascii($"<< /Length {content.Length} >>\nstream\n"), .. content.ToArray(), .. Ascii("\nendstream")]);
        }

        var pdf = new MemoryStream();
        pdf.Write(Ascii("%PDF-1.4\n"));
        var offsets = new List<long>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(pdf.Position);
            pdf.Write(Ascii($"{i + 1} 0 obj\n"));
            pdf.Write(objects[i]);
            pdf.Write(Ascii("\nendobj\n"));
        }
        var xref = pdf.Position;
        pdf.Write(Ascii($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n"));
        foreach (var offset in offsets) pdf.Write(Ascii($"{offset:0000000000} 00000 n \n"));
        pdf.Write(Ascii($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n"));
        return pdf.ToArray();
    }

    /// Roughly the width of the text in Helvetica: good enough to right-align a cell.
    public static double Width(string text, double size) => text.Sum(c => c switch
    {
        ' ' or '.' or ',' or '/' or 'i' or 'l' or 'j' or 't' or 'f' or 'I' or '\'' => 0.278,
        '-' or '(' or ')' or 'r' => 0.333,
        >= 'A' and <= 'Z' => 0.69,
        _ => 0.556,
    }) * size;

    /// The width of the text in Helvetica by the font's own metrics, for a statement whose columns are found by
    /// the right edge of right-aligned text. Only unaccented letters, digits and the punctuation of amounts and
    /// dates.
    public static double ExactWidth(string text, double size) => text.Sum(c => c switch
    {
        >= '0' and <= '9' => 556,
        ' ' or '.' or ',' or '/' or ':' => 278,
        '-' => 333,
        >= 'a' and <= 'z' => Lower[c - 'a'],
        >= 'A' and <= 'Z' => Upper[c - 'A'],
        _ => throw new ArgumentException($"The width of '{c}' isn't known."),
    }) * size / 1000;

    private static readonly int[] Lower =
        [556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556, 556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500];

    private static readonly int[] Upper =
        [667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778, 667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611];

    private static byte WinAnsi(char c) => c switch
    {
        '€' => 0x80,
        < (char)0x80 or (>= (char)0xA0 and <= (char)0xFF) => (byte)c,
        _ => throw new ArgumentException($"'{c}' can't be written to the synthetic statement."),
    };

    private static byte[] Ascii(string value) => Encoding.ASCII.GetBytes(value);
}
