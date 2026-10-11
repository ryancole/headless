using System.Collections.Generic;
using System.Linq;
using Headless.Protocol;

namespace Headless.Game;

/// <summary>An in-world terminal showing the commands the ship's bot sends to its devices, newest at the bottom.</summary>
public partial class CommandLogScreen : TerminalScreen
{
    private const int VisibleLines = 8;

    /// <summary>Which ship's log to show. Entries for other ships are ignored.</summary>
    public int ShipId
    {
        get => _shipId;
        set
        {
            if (_shipId == value) return;
            _shipId = value;
            Clear();
        }
    }

    private readonly List<string> _lines = [];
    private int _shipId;
    private uint _lastEntryId;

    /// <summary>Adds the entries for <see cref="ShipId"/> that haven't been shown yet.</summary>
    public void Show(ShipLog log)
    {
        var entries = log.Entries.Where(e => e.ShipId == ShipId).ToList();
        if (entries.Count > 0 && entries[^1].Id < _lastEntryId) Clear(); // Ids went backwards: the server restarted.

        var fresh = entries.Where(e => e.Id > _lastEntryId).ToList();
        if (fresh.Count == 0) return;

        // Ids count up by one per command, so a jump means entries we never saw (the server keeps only the latest).
        if (_lastEntryId != 0 && fresh[0].Id != _lastEntryId + 1) _lines.Add($"[color={Dim}]  ...[/color]");
        _lines.AddRange(fresh.Select(Describe));
        _lastEntryId = fresh[^1].Id;
        if (_lines.Count > VisibleLines) _lines.RemoveRange(0, _lines.Count - VisibleLines);
        Refresh();
    }

    /// <summary>One line, e.g. "  412 ENG SET  0.60", plus why if the ship refused it.</summary>
    private static string Describe(ShipLogEntry e)
    {
        var device = e.Kind switch { DeviceKind.Engine => "ENG", DeviceKind.Gyroscope => "GYR", _ => $"D{e.Device}" };
        var command = e.Command == DeviceCommands.Set ? "SET" : $"C{e.Command}";
        var line = $"{e.Tick,5} {device} {command} {e.Value + 0.0,5:0.00}"; // + 0.0 turns -0 into 0
        var refusal = e.Result switch
        {
            CommandResult.Ok => null,
            CommandResult.OverBudget => "BUDGET",
            CommandResult.InvalidValue => "INVALID",
            CommandResult.UnknownCommand => "UNKNOWN",
            _ => e.Result.ToString().ToUpperInvariant(),
        };
        return refusal is null ? line : Colored(Warning, $"{line} {refusal}");
    }

    private void Clear()
    {
        _lines.Clear();
        _lastEntryId = 0;
        Refresh();
    }

    protected override void Redraw()
    {
        var header = Colored(Header, $"CMD LOG{(ShipId != 0 ? $"  SHIP {ShipId}" : "")}");
        var body = _lines.Count > 0 ? string.Join("\n", _lines) : $"[color={Dim}]NO COMMANDS YET[/color]";
        SetText(header + "\n" + body);
    }
}
