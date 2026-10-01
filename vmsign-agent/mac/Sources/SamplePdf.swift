import Foundation

/// Builds the one-page PDF that Test Sign sends to the signing server.
///
/// Written by hand, the same file the Windows agent's SamplePdf.cs builds: the server does all
/// of the signing work, so all this needs is a small, valid file with correct xref offsets.
/// Text is restricted to ASCII because the page uses the standard Helvetica font without
/// embedding one.
enum SamplePdf {
    static func build(title: String, lines: [String]) -> Data {
        var content = "BT /F1 18 Tf 72 780 Td (\(escape(title))) Tj ET\n"
        var y = 750
        for line in lines {
            content += "BT /F1 11 Tf 72 \(y) Td (\(escape(line))) Tj ET\n"
            y -= 18
        }

        // A4 in points. The server places signature boxes from the lower-left corner.
        let objects = [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] "
                + "/Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            "<< /Length \(content.utf8.count) >>\nstream\n\(content)endstream",
        ]

        var pdf = "%PDF-1.4\n"
        var offsets: [Int] = []
        for (index, object) in objects.enumerated() {
            offsets.append(pdf.utf8.count) // ASCII only, so characters == bytes
            pdf += "\(index + 1) 0 obj\n\(object)\nendobj\n"
        }

        let xref = pdf.utf8.count
        pdf += "xref\n0 \(objects.count + 1)\n"
        // Each entry is exactly 20 bytes, which is why the line ends in " \n".
        pdf += "0000000000 65535 f \n"
        for offset in offsets {
            pdf += String(format: "%010ld 00000 n \n", offset)
        }
        pdf += "trailer\n<< /Size \(objects.count + 1) /Root 1 0 R >>\nstartxref\n\(xref)\n%%EOF\n"
        return Data(pdf.utf8)
    }

    /// Drops Vietnamese diacritics and anything else Helvetica cannot show.
    static func toAscii(_ text: String) -> String {
        let stripped = text
            .replacingOccurrences(of: "đ", with: "d")
            .replacingOccurrences(of: "Đ", with: "D")
            .applyingTransform(.stripDiacritics, reverse: false) ?? text
        let chars: [Character] = stripped.unicodeScalars.map { scalar in
            scalar.value >= 0x20 && scalar.value < 0x7F ? Character(scalar) : "?"
        }
        return String(chars)
    }

    private static func escape(_ text: String) -> String {
        toAscii(text)
            .replacingOccurrences(of: "\\", with: "\\\\")
            .replacingOccurrences(of: "(", with: "\\(")
            .replacingOccurrences(of: ")", with: "\\)")
    }
}
