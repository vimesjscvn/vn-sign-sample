using iText.Forms;
using iText.Forms.Fields;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using System;
using System.Collections.Generic;
using System.IO;
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
                using (var reader = new PdfReader(pdfPath))
                using (var writer = new PdfWriter(tmpPath))
                using (var pdfDoc = new PdfDocument(reader, writer))
                {
                    var acroForm = PdfAcroForm.GetAcroForm(pdfDoc, true);
                    var existingFields = acroForm.GetFormFields();

                    foreach (var label in labels)
                    {
                        // 1. Duplicate check
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
                        // 2. Search text across all pages
                        for (int page = pdfDoc.GetNumberOfPages(); page >= 1; page--)
                        {
                            var pageObj = pdfDoc.GetPage(page);
                            var pageHeight = pageObj.GetPageSize().GetHeight();
                            
                            LocationTextExtractionStrategyEx strategy = new LocationTextExtractionStrategyEx(
                                label.Label, pageHeight);
                            
                            PdfTextExtractor.GetTextFromPage(pageObj, strategy);

                            if (strategy.m_SearchResultsList.Count > 0)
                            {
                                var match = strategy.m_SearchResultsList[0];
                                float textWidth = match.iPosRX - match.iPosLX;
                                float fieldX = match.iPosLX + (textWidth / 2f) - (label.Width / 2f);
                                // Place signature field below the text
                                float fieldY = match.iPosLY - label.OffsetY - label.Height;

                                // Prevent field from falling off the page
                                if (fieldY < 0) fieldY = 5;

                                // 3. Create empty signature field
                                var sigField = PdfFormField.CreateSignature(pdfDoc, new Rectangle(fieldX, fieldY, label.Width, label.Height));
                                sigField.SetFieldName(label.FieldName);
                                
                                acroForm.AddField(sigField, pageObj);

                                Log.Information("Auto-created signature field {FieldName} on page {Page} at X={X}, Y={Y}", 
                                    label.FieldName, page, fieldX, fieldY);

                                results.Add(new CreatedFieldResult
                                {
                                    FieldName = label.FieldName,
                                    Page = page,
                                    X = fieldX,
                                    Y = pageHeight - (fieldY + label.Height), // top-down coordinate for UI
                                    Width = label.Width,
                                    Height = label.Height
                                });
                                found = true;
                                break; // Found, move to next label
                            }
                        }

                        if (!found)
                        {
                            Log.Information("Label '{Label}' not found in PDF.", label.Label);
                        }
                    }
                }

                // If any fields were created, replace the file
                if (results.Exists(r => !r.WasSkipped))
                {
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
