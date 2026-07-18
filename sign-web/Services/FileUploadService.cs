namespace VMSign.Web.Services;

/// <summary>
/// Handles file upload and temporary storage for signing operations.
/// </summary>
public class FileUploadService
{
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<FileUploadService> _logger;

    public FileUploadService(IWebHostEnvironment env, ILogger<FileUploadService> logger)
    {
        _env = env;
        _logger = logger;
    }

    /// <summary>
    /// Save an uploaded file to a temp directory. Returns the saved path.
    /// </summary>
    public async Task<string> SaveUploadedFileAsync(IFormFile file)
    {
        var uploadDir = Path.Combine(_env.ContentRootPath, "temp", "uploads");
        Directory.CreateDirectory(uploadDir);

        // Use a unique name to avoid collisions
        var fileName = $"{Guid.NewGuid():N}_{file.FileName}";
        var filePath = Path.Combine(uploadDir, fileName);

        await using var stream = new FileStream(filePath, FileMode.Create);
        await file.CopyToAsync(stream);

        _logger.LogInformation("Saved upload: {FileName} → {Path}", file.FileName, filePath);
        return filePath;
    }

    /// <summary>
    /// Clean up temp files older than the specified age.
    /// </summary>
    public void CleanupOldFiles(TimeSpan maxAge)
    {
        var uploadDir = Path.Combine(_env.ContentRootPath, "temp", "uploads");
        if (!Directory.Exists(uploadDir)) return;

        var cutoff = DateTime.UtcNow - maxAge;
        foreach (var file in Directory.GetFiles(uploadDir))
        {
            if (File.GetCreationTimeUtc(file) < cutoff)
            {
                File.Delete(file);
                _logger.LogDebug("Cleaned up temp file: {File}", file);
            }
        }
    }
}
