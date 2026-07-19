using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace VMSign.Web.Services
{
    public class LocalVisionLayoutService
    {
        private static Serilog.ILogger Log => Serilog.Log.ForContext<LocalVisionLayoutService>();
        private readonly string _apiUrl;
        private readonly string _engineType;
        private readonly string _model;
        private readonly bool _enabled;
        private readonly HttpClient _httpClient;

        public class DetectionResult
        {
            [JsonProperty("label")]
            public string Label { get; set; }

            [JsonProperty("box_2d")]
            public int[] Box2d { get; set; } // [ymin, xmin, ymax, xmax] (0-1000)
        }

        public LocalVisionLayoutService(IConfiguration configuration)
        {
            var section = configuration.GetSection("LocalLayoutSetting");
            _enabled = section.GetValue<bool>("Enable");
            _apiUrl = section["ApiUrl"] ?? "http://localhost:8000/detect";
            _engineType = section["EngineType"] ?? "GenericFastApi";
            _model = section["Model"] ?? "nvidia/LocateAnything-3B";
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(60); // Local models might take time
        }

        public bool IsEnabled => _enabled;

        public async Task<List<DetectionResult>> DetectSignatureBlocksAsync(byte[] bmpBytes)
        {
            var results = new List<DetectionResult>();
            if (!_enabled) return results;

            try
            {
                // Convert BMP to JPEG to reduce payload size
                byte[] jpegBytes;
                using (var ms = new MemoryStream())
                {
                    using (var image = System.Drawing.Image.FromStream(new MemoryStream(bmpBytes)))
                    {
                        image.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
                    }
                    jpegBytes = ms.ToArray();
                }

                string base64Image = Convert.ToBase64String(jpegBytes);
                string jsonPayload;

                if (string.Equals(_engineType, "Ollama", StringComparison.OrdinalIgnoreCase))
                {
                    var payload = new
                    {
                        model = _model,
                        messages = new[]
                        {
                            new
                            {
                                role = "user",
                                content = "Analyze this page of a Vietnamese document. Detect all signature fields (where signatures or stamps should go, e.g. under 'NGƯỜI LẬP BẢNG', 'KẾ TOÁN', 'XÁC NHẬN CỦA KHÁCH HÀNG', etc.). Return ONLY a valid JSON array of objects, each containing: 'label' (signer role) and 'box_2d' (bounding box [ymin, xmin, ymax, xmax] normalized 0-1000). Do not output markdown code blocks or explanatory text.",
                                images = new[] { base64Image }
                            }
                        },
                        stream = false,
                        format = "json"
                    };
                    jsonPayload = JsonConvert.SerializeObject(payload);
                }
                else
                {
                    // Generic FastAPI / Custom Python script format
                    var payload = new
                    {
                        image = base64Image,
                        prompt = "signature blocks"
                    };
                    jsonPayload = JsonConvert.SerializeObject(payload);
                }

                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(_apiUrl, content);
                string responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Log.Error("Local visual model returned error (Status {Status}): {Body}", response.StatusCode, responseBody);
                    return results;
                }

                if (string.Equals(_engineType, "Ollama", StringComparison.OrdinalIgnoreCase))
                {
                    dynamic respObj = JsonConvert.DeserializeObject(responseBody);
                    string rawText = respObj.message.content;
                    rawText = rawText.Trim();
                    results = JsonConvert.DeserializeObject<List<DetectionResult>>(rawText) ?? new List<DetectionResult>();
                }
                else
                {
                    results = JsonConvert.DeserializeObject<List<DetectionResult>>(responseBody) ?? new List<DetectionResult>();
                }

                Log.Information("Local visual layout model detected {Count} signature blocks.", results.Count);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to run local visual layout detection");
            }

            return results;
        }
    }
}
