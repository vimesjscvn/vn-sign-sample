using iText.Forms;
using iText.Forms.Fields;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Serilog;

namespace VMSign.Web.Services
{
    public class TextSearchFieldCreator
    {
        private static Serilog.ILogger Log => Serilog.Log.ForContext<TextSearchFieldCreator>();

        public class SignerLabel
        {
            public string Label { get; set; }      // "Người bệnh"
            public string FieldName { get; set; }   // "sig_nguoibenh"
            public float Width { get; set; } = 200f;
            public float Height { get; set; } = 80f;
            public float OffsetY { get; set; } = 40f; // gap below label to clear titles and subtitles
        }

        public class CreatedFieldResult
        {
            public string FieldName { get; set; }
            public int Page { get; set; }
            public float X { get; set; }
            public float Y { get; set; }
            public float Width { get; set; }
            public float Height { get; set; }
            public bool WasSkipped { get; set; }
            public string SkipReason { get; set; }
        }

        // ─── Collision Detection Helpers ────────────────────────────────

        /// <summary>
        /// A simple bounding box representing a text chunk on the page.
        /// Coordinates are in PDF coordinate space (origin at bottom-left).
        /// </summary>
        public class TextRect
        {
            public float Left { get; set; }
            public float Bottom { get; set; }
            public float Right { get; set; }
            public float Top { get; set; }

            public TextRect(float left, float bottom, float right, float top)
            {
                Left = left;
                Bottom = bottom;
                Right = right;
                Top = top;
            }
        }

        /// <summary>
        /// Listener that collects bounding boxes of every rendered text chunk on a page.
        /// </summary>
        private class TextRectExtractor : IEventListener
        {
            public List<TextRect> TextRects { get; } = new List<TextRect>();

            public void EventOccurred(IEventData data, EventType type)
            {
                if (type != EventType.RENDER_TEXT) return;
                var renderInfo = (TextRenderInfo)data;
                var text = renderInfo.GetText()?.Trim();
                if (string.IsNullOrEmpty(text)) return;

                var baseline = renderInfo.GetBaseline();
                var ascentLine = renderInfo.GetAscentLine();
                var descentLine = renderInfo.GetDescentLine();

                float left = Math.Min(baseline.GetStartPoint().Get(Vector.I1),
                                      baseline.GetEndPoint().Get(Vector.I1));
                float right = Math.Max(baseline.GetStartPoint().Get(Vector.I1),
                                       baseline.GetEndPoint().Get(Vector.I1));
                float bottom = descentLine.GetStartPoint().Get(Vector.I2);
                float top = ascentLine.GetStartPoint().Get(Vector.I2);

                // Skip degenerate rects
                if (right - left < 1f || top - bottom < 1f) return;

                TextRects.Add(new TextRect(left, bottom, right, top));
            }

            public ICollection<EventType> GetSupportedEvents()
            {
                return new HashSet<EventType> { EventType.RENDER_TEXT };
            }
        }

        /// <summary>
        /// Extract all text bounding boxes from a PDF page.
        /// </summary>
        public static List<TextRect> ExtractAllTextRects(iText.Kernel.Pdf.PdfPage page)
        {
            var extractor = new TextRectExtractor();
            new PdfCanvasProcessor(extractor).ProcessPageContent(page);
            return extractor.TextRects;
        }

        /// <summary>
        /// Check if a proposed signature rectangle collides with any text rectangle.
        /// Uses a padding margin to keep a visual gap between text and signature box.
        /// </summary>
        public static bool HasCollision(float sigLeft, float sigBottom, float sigRight, float sigTop,
                                        List<TextRect> textRects, float padding = 3f)
        {
            foreach (var tr in textRects)
            {
                // Expand text rect by padding on all sides
                float tLeft = tr.Left - padding;
                float tBottom = tr.Bottom - padding;
                float tRight = tr.Right + padding;
                float tTop = tr.Top + padding;

                // Standard AABB intersection test
                bool overlaps = sigLeft < tRight && sigRight > tLeft &&
                                sigBottom < tTop && sigTop > tBottom;
                if (overlaps) return true;
            }
            return false;
        }

        /// <summary>
        /// Starting from an initial position, nudge the signature rectangle
        /// down/up/left/right until it finds a position that doesn't collide
        /// with any text on the page.
        /// Only considers text rects that are within a vertical vicinity of the
        /// initial placement to avoid being pushed away by distant table data.
        /// </summary>
        public static (float x, float y) NudgeToAvoidCollision(
            float initialX, float initialY, float width, float height,
            List<TextRect> textRects, float pageWidth, float pageHeight)
        {
            const float stepY = 5f;
            const float stepX = 10f;
            const int maxDownSteps = 15;   // up to 75pt down (prevents over-displacement)
            const int maxUpSteps = 10;     // up to 50pt up
            const int maxHorizSteps = 10;  // up to 100pt left/right

            // Filter text rects: only consider text that is vertically within 
            // a reasonable range around the initial placement position.
            // This prevents distant table data or text in unrelated regions from
            // triggering excessive nudging.
            float vicinityMargin = 120f; // only check text within 120pt above/below initial position
            float sigTop = initialY + height;
            float sigBottom = initialY;
            var nearbyTextRects = textRects.FindAll(tr =>
                tr.Top > (sigBottom - vicinityMargin) &&
                tr.Bottom < (sigTop + vicinityMargin) &&
                // Also filter horizontally: only text that horizontally overlaps or is near the signature column
                tr.Right > (initialX - 20f) &&
                tr.Left < (initialX + width + 20f)
            );

            Log.Information("Collision check: {NearbyCount}/{TotalCount} text rects in vicinity of ({X},{Y})",
                nearbyTextRects.Count, textRects.Count, initialX, initialY);

            // Helper: check if candidate is collision-free and within page bounds
            bool TryPosition(float cx, float cy, out float ox, out float oy)
            {
                ox = cx; oy = cy;
                // Page boundary check (PDF coords: Y=0 at bottom)
                if (cy < 5f || cy + height > pageHeight - 5f) return false;
                if (cx < 5f || cx + width > pageWidth - 5f) return false;

                return !HasCollision(cx, cy, cx + width, cy + height, nearbyTextRects);
            }

            // 1. Try original position first
            if (TryPosition(initialX, initialY, out var rx, out var ry))
                return (rx, ry);

            // 2. Try moving DOWN (decreasing Y in PDF coords)
            for (int step = 1; step <= maxDownSteps; step++)
            {
                if (TryPosition(initialX, initialY - step * stepY, out rx, out ry))
                {
                    Log.Information("Nudged DOWN by {Points}pt to avoid text collision", step * stepY);
                    return (rx, ry);
                }
            }

            // 3. Try moving UP (increasing Y in PDF coords)
            for (int step = 1; step <= maxUpSteps; step++)
            {
                if (TryPosition(initialX, initialY + step * stepY, out rx, out ry))
                {
                    Log.Information("Nudged UP by {Points}pt to avoid text collision", step * stepY);
                    return (rx, ry);
                }
            }

            // 4. Try shifting LEFT
            for (int step = 1; step <= maxHorizSteps; step++)
            {
                if (TryPosition(initialX - step * stepX, initialY, out rx, out ry))
                {
                    Log.Information("Nudged LEFT by {Points}pt to avoid text collision", step * stepX);
                    return (rx, ry);
                }
            }

            // 5. Try shifting RIGHT
            for (int step = 1; step <= maxHorizSteps; step++)
            {
                if (TryPosition(initialX + step * stepX, initialY, out rx, out ry))
                {
                    Log.Information("Nudged RIGHT by {Points}pt to avoid text collision", step * stepX);
                    return (rx, ry);
                }
            }

            // 6. Fallback: return original position (best effort)
            Log.Warning("Could not find collision-free position, using original placement");
            return (initialX, initialY);
        }

        // ─── Main Field Creation ────────────────────────────────────────

        public static List<CreatedFieldResult> CreateFieldsFromLabels(
            string pdfPath, 
            List<SignerLabel> labels)
        {
            var results = new List<CreatedFieldResult>();
            if (labels == null || labels.Count == 0)
            {
                return results;
            }

            string tmpPath = pdfPath + ".tmp";
            try
            {
                // ── Pass 1: Calculate collision-free positions (read-only PDF scan) ──
                var placements = new List<(SignerLabel label, int page, float x, float y)>();

                using (var reader = new PdfReader(pdfPath))
                using (var pdfDoc = new PdfDocument(reader))
                {
                    var acroForm = PdfAcroForm.GetAcroForm(pdfDoc, false);
                    var existingFields = acroForm?.GetFormFields() ?? new Dictionary<string, PdfFormField>();

                    // Cache text rects per page to avoid re-extracting
                    var pageTextRectsCache = new Dictionary<int, List<TextRect>>();

                    foreach (var label in labels)
                    {
                        // Duplicate check
                        if (existingFields.ContainsKey(label.FieldName))
                        {
                            Log.Information("Field {FieldName} already exists in PDF, skipping auto-creation.", label.FieldName);
                            results.Add(new CreatedFieldResult
                            {
                                FieldName = label.FieldName,
                                WasSkipped = true,
                                SkipReason = "Field already exists"
                            });
                            continue;
                        }

                        bool found = false;
                        for (int page = pdfDoc.GetNumberOfPages(); page >= 1; page--)
                        {
                            var pageObj = pdfDoc.GetPage(page);
                            var pageHeight = pageObj.GetPageSize().GetHeight();
                            var pageWidth = pageObj.GetPageSize().GetWidth();

                            LocationTextExtractionStrategyEx strategy = new LocationTextExtractionStrategyEx(
                                label.Label, pageHeight);

                            PdfTextExtractor.GetTextFromPage(pageObj, strategy);

                            if (strategy.m_SearchResultsList.Count > 0)
                            {
                                var match = strategy.m_SearchResultsList[0];

                                // Calculate initial centered position below the label text
                                float textWidth = match.iPosRX - match.iPosLX;
                                float textCenterX = match.iPosLX + (textWidth / 2f);
                                float fieldX = textCenterX - (label.Width / 2f);
                                float fieldY = match.iPosLY - label.OffsetY - label.Height;

                                // Extract all text rects on this page (cached)
                                if (!pageTextRectsCache.ContainsKey(page))
                                {
                                    pageTextRectsCache[page] = ExtractAllTextRects(pageObj);
                                    Log.Information("Extracted {Count} text bounding boxes on page {Page}",
                                        pageTextRectsCache[page].Count, page);
                                }
                                var textRects = pageTextRectsCache[page];

                                // Nudge to avoid collision with any text
                                var (nudgedX, nudgedY) = NudgeToAvoidCollision(
                                    fieldX, fieldY, label.Width, label.Height,
                                    textRects, pageWidth, pageHeight);

                                // Prevent field from falling off the page
                                if (nudgedY < 5) nudgedY = 5;
                                if (nudgedX < 5) nudgedX = 5;
                                if (nudgedX + label.Width > pageWidth - 5)
                                    nudgedX = pageWidth - label.Width - 5;

                                placements.Add((label, page, nudgedX, nudgedY));
                                Log.Information("Placed {FieldName} on page {Page} at X={X}, Y={Y} (collision-free)",
                                    label.FieldName, page, nudgedX, nudgedY);

                                found = true;
                                break;
                            }
                        }

                        if (!found)
                        {
                            Log.Information("Label '{Label}' not found in PDF.", label.Label);
                        }
                    }
                }

                // ── Pass 2: Row-align fields on the same page ──
                // Group fields by page, then by Y proximity, and snap to common Y
                var pageGroups = placements.GroupBy(p => p.page);
                foreach (var pageGroup in pageGroups)
                {
                    var items = pageGroup.ToList();
                    var rowGroups = new List<List<int>>(); // indices into 'items'

                    for (int i = 0; i < items.Count; i++)
                    {
                        bool placed = false;
                        foreach (var row in rowGroups)
                        {
                            float rowAvgY = row.Average(idx => items[idx].y);
                            if (Math.Abs(items[i].y - rowAvgY) < 30f) // within 30pt = same visual row
                            {
                                row.Add(i);
                                placed = true;
                                break;
                            }
                        }
                        if (!placed) rowGroups.Add(new List<int> { i });
                    }

                    // Snap each row to the average Y
                    foreach (var row in rowGroups)
                    {
                        if (row.Count <= 1) continue;
                        float avgY = row.Average(idx => items[idx].y);
                        Log.Information("Row-aligning {Count} fields to common Y={AvgY}", row.Count, avgY);
                        foreach (var idx in row)
                        {
                            var item = items[idx];
                            items[idx] = (item.label, item.page, item.x, avgY);
                        }
                    }

                    // Write aligned items back to placements
                    int placementIdx = 0;
                    for (int i = 0; i < placements.Count; i++)
                    {
                        if (placements[i].page == pageGroup.Key)
                        {
                            placements[i] = items[placementIdx++];
                        }
                    }
                }

                // ── Pass 3: Write all fields to PDF ──
                if (placements.Count > 0)
                {
                    using (var reader = new PdfReader(pdfPath))
                    using (var writer = new PdfWriter(tmpPath))
                    using (var pdfDoc = new PdfDocument(reader, writer))
                    {
                        var acroForm = PdfAcroForm.GetAcroForm(pdfDoc, true);

                        foreach (var (label, page, x, y) in placements)
                        {
                            var pageObj = pdfDoc.GetPage(page);
                            var pageHeight = pageObj.GetPageSize().GetHeight();

                            var sigField = PdfFormField.CreateSignature(pdfDoc,
                                new Rectangle(x, y, label.Width, label.Height));
                            sigField.SetFieldName(label.FieldName);
                            acroForm.AddField(sigField, pageObj);

                            Log.Information("Written signature field {FieldName} on page {Page} at X={X}, Y={Y}",
                                label.FieldName, page, x, y);

                            results.Add(new CreatedFieldResult
                            {
                                FieldName = label.FieldName,
                                Page = page,
                                X = x,
                                Y = pageHeight - (y + label.Height), // top-down coordinate for UI
                                Width = label.Width,
                                Height = label.Height
                            });
                        }
                    }

                    File.Delete(pdfPath);
                    File.Move(tmpPath, pdfPath);
                }
                else
                {
                    if (File.Exists(tmpPath))
                    {
                        File.Delete(tmpPath);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to auto-create signature fields in PDF {PdfPath}", pdfPath);
                if (File.Exists(tmpPath))
                {
                    try { File.Delete(tmpPath); } catch {}
                }
                throw;
            }

            return results;
        }
    }
}
