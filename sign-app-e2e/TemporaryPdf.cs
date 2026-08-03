using System.Text;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.IO.Font.Constants;

namespace VMSign.AppE2E;

internal static class TemporaryPdf
{
    public static string CreateE2ETestOnlyDocument(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"e2e-test-only-{Guid.NewGuid():N}.pdf");

        using var writer = new PdfWriter(path);
        using var pdf = new PdfDocument(writer);
        var page = pdf.AddNewPage(iText.Kernel.Geom.PageSize.A4);
        var canvas = new PdfCanvas(page);
        var font = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);

        canvas.BeginText()
            .SetFontAndSize(font, 32)
            .MoveText(155, 700)
            .ShowText("E2E TEST ONLY")
            .EndText();

        return path;
    }

    public static string CreateTwoPageDocument()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vmsign-e2e-{Guid.NewGuid():N}.pdf");
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> /Contents 5 0 R >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << >> /Contents 5 0 R >>",
            "<< /Length 0 >>\nstream\n\nendstream",
        };

        using var stream = new MemoryStream();
        WriteAscii(stream, "%PDF-1.4\n");
        var offsets = new List<long> { 0 };

        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(stream.Position);
            WriteAscii(stream, $"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }

        var xrefOffset = stream.Position;
        WriteAscii(stream, $"xref\n0 {objects.Length + 1}\n");
        WriteAscii(stream, "0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
        {
            WriteAscii(stream, $"{offset:0000000000} 00000 n \n");
        }

        WriteAscii(
            stream,
            $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");

        File.WriteAllBytes(path, stream.ToArray());
        return path;
    }

    private static void WriteAscii(Stream stream, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        stream.Write(bytes, 0, bytes.Length);
    }
}
