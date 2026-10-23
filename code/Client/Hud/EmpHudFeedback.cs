using System.Globalization;
using Trackstorm.Core.Items;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Hud;

/// <summary>Read-only independent EMP clocks and brief membership feedback from accepted authority.</summary>
internal sealed class EmpHudFeedback
{
    private readonly Dictionary<ulong, int> _labels = [];
    private (ulong Session, ulong Vehicle, ulong Life) _identity;
    private string _feedback = string.Empty;
    private double _feedbackUntil;

    internal string Project(VehicleSnapshot? vehicle, IReadOnlyList<EmpClawAttachment> claws,
        ulong tick, ulong session, double milliseconds)
    {
        var identity = (session, vehicle?.VehicleId ?? 0, vehicle?.LifeId ?? 0);
        bool reseed = identity != _identity || vehicle?.CanInteract != true;
        if (reseed)
        {
            _identity = identity;
            _labels.Clear();
            _feedback = string.Empty;
        }
        if (vehicle?.CanInteract != true) return string.Empty;
        var active = claws.Where(c => c.Vehicle == vehicle.VehicleId && c.Life == vehicle.LifeId && c.ExpiresAtTick > tick)
            .OrderBy(c => c.AttachedAtTick).ThenBy(c => c.Id).ToArray();
        var ids = active.Select(c => c.Id).ToHashSet();
        int removed = _labels.Keys.Count(id => !ids.Contains(id));
        foreach (ulong id in _labels.Keys.Where(id => !ids.Contains(id)).ToArray()) _labels.Remove(id);
        foreach (var claw in active)
        {
            if (_labels.ContainsKey(claw.Id)) continue;
            int label = 1;
            while (_labels.ContainsValue(label)) label++;
            _labels.Add(claw.Id, label);
            _feedback = string.Empty;
        }
        if (!reseed && removed > 0)
        {
            _feedback = active.Length == 0 ? "EMP CLEARED · POWER RESTORED" : $"{removed} CLAW REMOVED · {active.Length} ACTIVE";
            _feedbackUntil = milliseconds + 1800;
        }
        string text = string.Empty;
        if (active.Length > 0)
        {
            string clocks = string.Join("   ", active.Take(3).Select(c => string.Create(CultureInfo.InvariantCulture,
                $"{_labels[c.Id]}: {(c.ExpiresAtTick - tick + 59) / 60}s")));
            if (active.Length > 3) clocks += $"   +{active.Length - 3}";
            text = $"EMP ACTIVE · {active.Length} CLAW{(active.Length == 1 ? "" : "S")}\n{clocks}";
        }
        if (milliseconds < _feedbackUntil && _feedback.Length > 0)
            text += (text.Length == 0 ? "" : "\n") + _feedback;
        return text;
    }
}
