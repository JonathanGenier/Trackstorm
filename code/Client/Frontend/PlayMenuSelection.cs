using Trackstorm.Client.Online;

namespace Trackstorm.Client.Frontend;

/// <summary>Presentation-only criteria and deliberate activation; no discovery or session ownership.</summary>
internal sealed class PlayMenuSelection
{
    internal const double DoubleAcceptSeconds = 0.45;
    private string? _armed;
    private double _armedAt;
    private string? _mouseArmed;
    internal string? Selected { get; private set; }
    internal string Filter { get; set; } = "All";

    internal static string[] Choices(IEnumerable<LobbyRow> rows)
    {
        string[] modes = rows.Select(row => row.GameMode).Where(mode => mode.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return ["All", "Open Only", "Locked Only", "Has Space", .. modes.Length > 1 ? modes.Select(mode => "Mode: " + mode) : []];
    }

    internal LobbyRow[] Apply(IEnumerable<LobbyRow> rows, string search) => rows.Where(row =>
        row.Name.Contains(search, StringComparison.OrdinalIgnoreCase) && (Filter switch
        {
            "Open Only" => row.Access == LobbyAccess.Public,
            "Locked Only" => row.Access == LobbyAccess.Locked,
            "Has Space" => row.Members < row.Capacity,
            "All" => true,
            _ => Filter == "Mode: " + row.GameMode,
        })).ToArray();

    internal void Select(string? id)
    {
        if (Selected != id) Reset();
        Selected = id;
    }

    internal void Reset() { _armed = null; _mouseArmed = null; }

    internal bool MousePress(LobbyRow row, bool doubleClick)
    {
        Select(row.Id);
        bool join = row.Joinable && doubleClick && _mouseArmed == row.Id;
        _mouseArmed = join || !row.Joinable ? null : row.Id;
        _armed = null;
        return join;
    }

    internal bool Accept(LobbyRow row, double now)
    {
        Select(row.Id);
        if (!row.Joinable) { Reset(); return false; }
        bool join = _armed == row.Id && now >= _armedAt && now - _armedAt <= DoubleAcceptSeconds;
        _armed = join ? null : row.Id;
        _armedAt = now;
        return join;
    }
}
