namespace VMSign.Shared.Models;

/// <summary>
/// Represents the state of a signature field in the two-stage signing flow.
/// </summary>
public enum PlacementState
{
    /// <summary>Field exists but user hasn't selected it for signing.</summary>
    Empty,

    /// <summary>User has selected this field — pending commit.</summary>
    Placed,

    /// <summary>Field has been signed successfully.</summary>
    Signed
}
