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
    public class GeminiLayoutService
    {
        private static Serilog.ILogger Log => Serilog.Log.ForContext<GeminiLayoutService>();
        private readonly string _apiKey;
        private readonly string _model;
        private readonly HttpClient _httpClient;

        public class GeminiDetectionResult
        {
            [JsonProperty("label")]
            public string Label { get; set; }

            [JsonProperty("box_2d")]
            public int[] Box2d { get; set; } // [ymin, xmin, ymax, xmax] (0-1000)
        }

        public GeminiLayoutService(IConfiguration configuration)
        {
            _apiKey = configuration["GeminiSetting:ApiKey"] ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");
            _model = configuration["GeminiSetting:Model"] ?? "gemini-2.5-flash";
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(60);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
        }

        private string GetGCloudAccessToken()
        {
            try
            {
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c gcloud auth print-access-token",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = System.Diagnostics.Process.Start(processInfo))
                {
                    process.WaitForExit();
                    if (process.ExitCode == 0)
                    {
                        string token = process.StandardOutput.ReadToEnd().Trim();
                        return token;
                    }
                    else
                    {
                        string error = process.StandardError.ReadToEnd().Trim();
                        Log.Warning("gcloud print-access-token failed: {Error}", error);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning("Failed to execute gcloud command: {Message}", ex.Message);
            }
            return null;
        }

        private string GetGCloudProjectId()
        {
            try
            {
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c gcloud config get-value project",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = System.Diagnostics.Process.Start(processInfo))
                {
                    process.WaitForExit();
                    if (process.ExitCode == 0)
                    {
                        string project = process.StandardOutput.ReadToEnd().Trim();
                        if (!string.IsNullOrEmpty(project) && project != "(unset)")
                        {
                            return project;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning("Failed to fetch gcloud project ID: {Message}", ex.Message);
            }
            return "signservice"; // Fallback project
        }

        public async Task<List<GeminiDetectionResult>> DetectSignatureBlocksAsync(byte[] bmpBytes)
        {
            var results = new List<GeminiDetectionResult>();

            string url;
            string token = null;

            bool hasApiKey = !string.IsNullOrEmpty(_apiKey) && !_apiKey.Contains("YOUR_GEMINI_API_KEY");

            if (hasApiKey)
            {
                url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";
            }
            else
            {
                // Fallback to Vertex AI using active gcloud CLI credentials
                Log.Information("Gemini API key is not configured in settings. Attempting gcloud auth fallback...");
                token = GetGCloudAccessToken();
                if (string.IsNullOrEmpty(token))
                {
                    Log.Error("Could not retrieve gcloud access token. Please run 'gcloud auth application-default login' or configure GeminiApiKey.");
                    return results;
                }

                string projectId = GetGCloudProjectId();
                Log.Information("Using active Google Cloud Project: {ProjectId}", projectId);
                url = $"https://us-central1-aiplatform.googleapis.com/v1/projects/{projectId}/locations/us-central1/publishers/google/models/{_model}:generateContent";
            }

            try
            {
                // Convert BMP to JPEG to reduce upload size
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

                var payload = new
                {
                    contents = new[]
                    {
                        new
                        {
                            role = "user",
                            parts = new object[]
                            {
                                new
                                {
                                    text = "You are an expert document layout analysis engine. Analyze this page from a Vietnamese document.\n" +
                                           "Locate all signature areas (the blank spaces where signatures/stamps are placed, usually below signer titles like 'NGƯỜI LẬP BẢNG', 'KẾ TOÁN', 'GIÁM ĐỐC', etc.).\n" +
                                           "For each signature area, output a JSON object with 'label' (the title/role) and 'box_2d' (bounding box [ymin, xmin, ymax, xmax] normalized on a scale of 0 to 1000 from top-left [0,0]).\n" +
                                           "Respond ONLY with a valid JSON array, do not include markdown blocks or explanation."
                                },
                                new
                                {
                                    inlineData = new
                                    {
                                        mimeType = "image/jpeg",
                                        data = base64Image
                                    }
                                }
                            }
                        }
                    },
                    generationConfig = new
                    {
                        responseMimeType = "application/json"
                    }
                };

                string jsonPayload = JsonConvert.SerializeObject(payload);
                
                using (var requestMsg = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    requestMsg.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                    
                    if (!hasApiKey && !string.IsNullOrEmpty(token))
                    {
                        requestMsg.Headers.Add("Authorization", $"Bearer {token}");
                    }

                    var response = await _httpClient.SendAsync(requestMsg);
                    string responseBody = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        Log.Error("Gemini/Vertex AI API error (Status {Status}): {Body}", response.StatusCode, responseBody);
                        return results;
                    }

                    // Parse Response
                    dynamic respObj = JsonConvert.DeserializeObject(responseBody);
                    string rawText = respObj.candidates[0].content.parts[0].text;
                    
                    // Clean markdown JSON formatting if Gemini returned it despite instructions
                    rawText = rawText.Trim();
                    if (rawText.StartsWith("```json")) rawText = rawText.Substring(7);
                    if (rawText.EndsWith("```")) rawText = rawText.Substring(0, rawText.Length - 3);
                    rawText = rawText.Trim();

                    results = JsonConvert.DeserializeObject<List<GeminiDetectionResult>>(rawText) ?? new List<GeminiDetectionResult>();
                    Log.Information("AI successfully detected {Count} signature blocks visually.", results.Count);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to execute visual layout detection");
            }

            return results;
        }
    }
}
