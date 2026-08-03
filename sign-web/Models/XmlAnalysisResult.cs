namespace VMSign.Web.Models;

/// <summary>
/// Result of analyzing an uploaded XML document to pre-fill the SignTag/ParentXPath/
/// ReferenceId signing options — mirrors sign-app's btnAnalyzeXml_Click (MainWindow.axaml.cs).
/// </summary>
public class XmlAnalysisResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>HOC_BA, TONG_KET, LY_LICH, or UNKNOWN.</summary>
    public string DocumentType { get; set; } = "UNKNOWN";

    public List<string> SignTags { get; set; } = new();
    public string? DefaultSignTag { get; set; }

    public List<string> ReferenceIds { get; set; } = new();
    public string? DefaultReferenceId { get; set; }

    public List<XmlParentXPathOption> ParentXPaths { get; set; } = new();
    public string? DefaultParentXPath { get; set; }

    public int ExistingSignatureCount { get; set; }

    /// <summary>Human-readable trace of the analysis, meant to be piped into the client log panel
    /// the same way sign-app's LogSystem/LogWarning/LogSuccess calls narrate the analysis.</summary>
    public List<XmlAnalysisLogEntry> Logs { get; set; } = new();
}

public class XmlParentXPathOption
{
    public string XPath { get; set; } = "";
    public string Label { get; set; } = "";

    /// <summary>The ReferenceId this XPath should sync the dropdown to when selected (HOC_BA only).</summary>
    public string? ReferenceId { get; set; }
}

public class XmlAnalysisLogEntry
{
    public string Level { get; set; } = "info"; // info | ok | warn | error
    public string Message { get; set; } = "";
}
