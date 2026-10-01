using System.Globalization;
using System.Text;

namespace VMSignAgent;

/// <summary>
/// Builds the one-page PDF that Test Sign sends to the signing server.
///
/// Written by hand rather than through a PDF library: the agent ships without one, and
/// the server does all of the signing work, so all this needs is a small, valid file
/// with correct xref offsets. Text is restricted to ASCII because the page uses the
/// standard Helvetica font without embedding one.
/// </summary>
internal static class SamplePdf
{
    // A4 in points. The server places signature boxes from the lower-left corner.
    public const int PageWidth = 595;
    public const int PageHeight = 842;

    public static byte[] Build(string title, IEnumerable<string> lines)
    {
        var content = new StringBuilder();
        content.Append("BT /F1 18 Tf 72 780 Td (").Append(Escape(title)).Append(") Tj ET\n");
        var y = 750;
        foreach (var line in lines)
        {
            content.Append("BT /F1 11 Tf 72 ").Append(y.ToString(CultureInfo.InvariantCulture))
                .Append(" Td (").Append(Escape(line)).Append(") Tj ET\n");
            y -= 18;
        }

        var stream = content.ToString();
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidth} {PageHeight}] " +
                "/Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(stream)} >>\nstream\n{stream}endstream",
        };

        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(pdf.Length); // ASCII only, so characters == bytes
            pdf.Append(i + 1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n");
        }

        var xref = pdf.Length;
        pdf.Append("xref\n0 ").Append(objects.Length + 1).Append('\n');
        // Each entry is exactly 20 bytes, which is why the line ends in " \n".
        pdf.Append("0000000000 65535 f \n");
        foreach (var offset in offsets)
            pdf.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        pdf.Append("trailer\n<< /Size ").Append(objects.Length + 1).Append(" /Root 1 0 R >>\n")
            .Append("startxref\n").Append(xref).Append("\n%%EOF\n");

        return Encoding.ASCII.GetBytes(pdf.ToString());
    }

    /// <summary>Drops Vietnamese diacritics and anything else Helvetica cannot show.</summary>
    public static string ToAscii(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var sb = new StringBuilder();
        foreach (var ch in text!.Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(ch >= 0x20 && ch < 0x7F ? ch : '?');
        }
        return sb.ToString();
    }

    private static string Escape(string text) =>
        ToAscii(text).Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
}
