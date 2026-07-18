using VMSign.Shared.Models;

namespace VMSign.Shared.Services;

/// <summary>
/// Manages the two-stage signing placement state.
/// Tracks which fields are placed (pending) vs signed vs empty.
/// Implements §4.1 auto-place for single-field documents.
/// </summary>
public class PlacementManager
{
    private readonly Dictionary<string, PlacementState> _fields = new();
    private bool _drawnBoxPlaced;

    public IReadOnlyDictionary<string, PlacementState> Fields => _fields;
    public bool DrawnBoxPlaced => _drawnBoxPlaced;

    public int PlacedCount => _fields.Values.Count(s => s == PlacementState.Placed)
                              + (_drawnBoxPlaced ? 1 : 0);

    public int SignedCount => _fields.Values.Count(s => s == PlacementState.Signed);

    public bool CanSign => PlacedCount > 0;

    public string PlacementHint
    {
        get
        {
            if (CanSign)
                return $"Đã chọn {PlacedCount} vị trí ký. Bấm nút bên dưới để thực hiện ký số.";
            if (SignedCount > 0)
                return $"Đã ký {SignedCount} vị trí. Chọn thêm ô hoặc vẽ ô mới để ký tiếp.";
            return "Bấm vào ô chữ ký có sẵn trên tài liệu, hoặc bật \"Vẽ ô ký\" để chọn vị trí ký.";
        }
    }

    /// <summary>
    /// Initialize fields from detected PDF form fields.
    /// </summary>
    public void LoadFields(IEnumerable<SignatureFieldInfo> fields)
    {
        _fields.Clear();
        _drawnBoxPlaced = false;

        foreach (var field in fields)
        {
            _fields[field.Id] = field.IsSigned
                ? PlacementState.Signed
                : PlacementState.Empty;
        }

        AutoPlaceSingleField();
    }

    /// <summary>
    /// §4.1: If only one empty field exists and nothing is manually placed,
    /// auto-place it so user can sign with a single click.
    /// </summary>
    public void AutoPlaceSingleField()
    {
        var emptyFields = _fields
            .Where(kv => kv.Value == PlacementState.Empty)
            .ToList();

        var manuallyPlaced = _fields.Values.Any(s => s == PlacementState.Placed);

        if (emptyFields.Count == 1 && !manuallyPlaced && !_drawnBoxPlaced)
        {
            _fields[emptyFields[0].Key] = PlacementState.Placed;
        }
    }

    /// <summary>
    /// Toggle a field between Empty and Placed.
    /// </summary>
    public void TogglePlacement(string fieldId)
    {
        if (!_fields.ContainsKey(fieldId)) return;
        if (_fields[fieldId] == PlacementState.Signed) return;

        _fields[fieldId] = _fields[fieldId] == PlacementState.Placed
            ? PlacementState.Empty
            : PlacementState.Placed;
    }

    /// <summary>
    /// Toggle the drawn box placement.
    /// </summary>
    public void ToggleDrawnBox()
    {
        _drawnBoxPlaced = !_drawnBoxPlaced;
    }

    /// <summary>
    /// Mark all placed fields as signed after successful signing.
    /// </summary>
    public void CommitSigned()
    {
        foreach (var key in _fields.Keys.ToList())
        {
            if (_fields[key] == PlacementState.Placed)
                _fields[key] = PlacementState.Signed;
        }
        _drawnBoxPlaced = false;
    }

    /// <summary>
    /// Reset all placements (not signed state).
    /// </summary>
    public void ClearPlacements()
    {
        foreach (var key in _fields.Keys.ToList())
        {
            if (_fields[key] == PlacementState.Placed)
                _fields[key] = PlacementState.Empty;
        }
        _drawnBoxPlaced = false;
    }
}
