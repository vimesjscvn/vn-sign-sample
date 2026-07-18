using Microsoft.AspNetCore.Mvc;
using Docnet.Core;
using Docnet.Core.Models;
using System.Runtime.InteropServices;

namespace VMSign.Web.Controllers;

/// <summary>
/// Renders PDF pages as BMP images for the browser preview.
/// Uses Docnet (pdfium) server-side — same as the desktop app.
/// </summary>
public class PdfController : Controller
{
    private readonly ILogger<PdfController> _logger;

    public PdfController(ILogger<PdfController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Renders a specific page of a PDF as a BMP image.
    /// GET /Pdf/RenderPage?path=...&page=1
    /// </summary>
    [HttpGet]
    public IActionResult RenderPage(string path, int page = 1)
    {
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
            return NotFound(new { error = "File not found" });

        try
        {
            using var library = DocLib.Instance;
            using var docReader = library.GetDocReader(path, new PageDimensions(1.0d));

            var pageCount = docReader.GetPageCount();
            if (page < 1) page = 1;
            if (page > pageCount) page = pageCount;

            using var pageReader = docReader.GetPageReader(page - 1);
            var width = pageReader.GetPageWidth();
            var height = pageReader.GetPageHeight();
            var rawBytes = pageReader.GetImage(
                RenderFlags.RenderAnnotations | (RenderFlags)0x800); // FPDF_PRINTING

            var bmpBytes = ConvertBgraToBmp(rawBytes, width, height);

            // Return page info in headers
            Response.Headers.Append("X-Page-Width", width.ToString());
            Response.Headers.Append("X-Page-Height", height.ToString());
            Response.Headers.Append("X-Page-Count", pageCount.ToString());
            Response.Headers.Append("X-Current-Page", page.ToString());

            return File(bmpBytes, "image/bmp");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to render PDF page {Page} of {Path}", page, path);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Returns PDF metadata (page count, dimensions).
    /// GET /Pdf/Info?path=...
    /// </summary>
    [HttpGet]
    public IActionResult Info(string path)
    {
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
            return NotFound(new { error = "File not found" });

        try
        {
            using var library = DocLib.Instance;
            using var docReader = library.GetDocReader(path, new PageDimensions(1.0d));
            var pageCount = docReader.GetPageCount();

            using var pageReader = docReader.GetPageReader(0);
            var width = pageReader.GetPageWidth();
            var height = pageReader.GetPageHeight();

            return Json(new { pageCount, width, height });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Downloads a PDF file from the server.
    /// GET /Pdf/Download?path=...
    /// </summary>
    [HttpGet]
    public IActionResult Download(string path)
    {
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
            return NotFound(new { error = "File not found" });

        try
        {
            var fileBytes = System.IO.File.ReadAllBytes(path);
            var fileName = System.IO.Path.GetFileName(path);
            return File(fileBytes, "application/pdf", fileName);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Wraps raw BGRA pixels from Docnet in a BMP file header (no SkiaSharp/ImageSharp
    /// dependency needed — browsers render BMP natively).
    /// </summary>
    private static byte[] ConvertBgraToBmp(byte[] bgra, int width, int height)
    {
        // BMP file header (14 bytes) + DIB header (40 bytes) = 54 bytes total
        int rowSize = width * 4; // BGRA = 4 bytes per pixel
        int imageSize = rowSize * height;
        int fileSize = 54 + imageSize;
        
        var bmp = new byte[fileSize];
        
        // BMP File Header
        bmp[0] = 0x42; bmp[1] = 0x4D; // "BM"
        BitConverter.GetBytes(fileSize).CopyTo(bmp, 2);
        BitConverter.GetBytes(54).CopyTo(bmp, 10); // pixel data offset
        
        // DIB Header (BITMAPINFOHEADER)
        BitConverter.GetBytes(40).CopyTo(bmp, 14); // header size
        BitConverter.GetBytes(width).CopyTo(bmp, 18);
        BitConverter.GetBytes(-height).CopyTo(bmp, 22); // negative = top-down
        BitConverter.GetBytes((short)1).CopyTo(bmp, 26); // planes
        BitConverter.GetBytes((short)32).CopyTo(bmp, 28); // bits per pixel
        BitConverter.GetBytes(0).CopyTo(bmp, 30); // compression (BI_RGB)
        BitConverter.GetBytes(imageSize).CopyTo(bmp, 34);
        
        // Copy pixel data (BGRA from docnet is already in BMP format)
        Buffer.BlockCopy(bgra, 0, bmp, 54, imageSize);
        
        return bmp;
    }
}
