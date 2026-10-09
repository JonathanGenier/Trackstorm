using System.Numerics;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Items;

public sealed partial class ItemAuthority
{
    private sealed record AimIntent(ulong Life, ulong Token, ulong Selection, ulong Sequence, ulong Received, Vector3 Direction);
    private readonly Dictionary<ulong, AimIntent> _aimIntents = new();
    private readonly Dictionary<ulong, WeaponAimSolution> _aims = new();
    private sealed record AimDeployment(ulong Life, ulong Token, ulong Selection, HeldItem Item, float Progress, bool Retracting, ulong Tick, float Nesting);
    private readonly Dictionary<ulong, AimDeployment> _aimDeployment = new();

    /// <summary>Detached accepted articulation; transient input is deliberately reset on recovery.</summary>
    public IReadOnlyList<WeaponAimSolution> Aims => _aims.Values.OrderBy(value => value.Vehicle).ToArray();

    /// <summary>Validates direction against actual sender inventory, life, selection and ordered input.</summary>
    public bool RequestAim(Simulation.Simulation world, ulong vehicle, ulong life, ulong token, ulong selection, ulong sequence, Vector3 direction)
    {
        var state = world.State.Vehicles.SingleOrDefault(value => value.VehicleId == vehicle);
        if (state is not { CanInteract: true } || state.LifeId != life || sequence == 0 || !WeaponAim.IsDirection(direction) ||
            !_slots.TryGetValue(vehicle, out var slot) || slot.Life != life || slot.Active.Token != token ||
            slot.SelectionRevision != selection || !WeaponAim.Supports(slot.Active.Item) ||
            (_aimIntents.TryGetValue(vehicle, out var previous) && previous.Life == life && previous.Sequence >= sequence)) { return false; }
        _aimIntents[vehicle] = new(life, token, selection, sequence, world.State.Tick, direction);
        return true;
    }

    /// <summary>Current solution for future direct-fire consumers; never falls back to a stale capability.</summary>
    public WeaponAimSolution? AcceptedAim(ulong vehicle, ulong life, ulong token) =>
        _aims.TryGetValue(vehicle, out var aim) && aim.Life == life && aim.Token == token && aim.Ready ? aim : null;

    /// <summary>Solves the current pre-command native pose batch; the host rolls back on a rejected step.</summary>
    public void AdvanceAim(Simulation.Simulation world, VehicleConfiguration vehicleConfiguration, bool participating, IReadOnlyList<VehicleStepRequest>? observations = null)
    {
        foreach (ulong id in _slots.Keys.Concat(_aims.Keys).Concat(_aimIntents.Keys).Concat(_aimDeployment.Keys).Distinct().ToArray())
        {
            var state = world.State.Vehicles.SingleOrDefault(value => value.VehicleId == id);
            if (!participating || state is not { CanInteract: true })
            { ResetAim(id); _aimDeployment.Remove(id); continue; }
            if (!_slots.TryGetValue(id, out var slot) || slot.Life != state.LifeId)
            { ResetAim(id); continue; }
            ulong tick = world.State.Tick;
            _aimDeployment.TryGetValue(id, out var deployment);
            if (deployment?.Life != state.LifeId) { deployment = null; }
            if (slot.Active.Item == HeldItem.None) { ResetAim(id); continue; }
            if (deployment is null || deployment.Token != slot.Active.Token || deployment.Selection != slot.SelectionRevision)
            {
                // Keep capability history through depletion and switches. A replacement
                // takes over a complete retract/deploy path, even without a slot switch.
                bool replacing = deployment is not null;
                deployment = new(state.LifeId, slot.Active.Token, slot.SelectionRevision, slot.Active.Item,
                    replacing ? 1 : 0, replacing, tick, deployment?.Item == HeldItem.Nitro ? .24f : 0);
                _aims.Remove(id);
            }
            if (!WeaponAim.Supports(slot.Active.Item))
            { _aimDeployment[id] = deployment; ResetAim(id); continue; }
            // An accepted mine arm owns the rack until placement and its return finish,
            // even if the selected inventory has already become a direct-fire weapon.
            if (_mines.Any(mine => mine.Owner == id && mine.PlacementLife == state.LifeId && mine.IsPlacing))
            { deployment = deployment with { Progress = 1, Retracting = true, Nesting = ProxyMineState.ArmReturnSeconds, Tick = tick }; }
            if (deployment.Tick < tick)
            {
                float elapsed = Math.Max(0, 1f / 60 - deployment.Nesting);
                float progress = RackDeploymentPath.Advance(deployment.Progress, !deployment.Retracting, elapsed, vehicleConfiguration);
                deployment = deployment with { Progress = progress, Retracting = deployment.Retracting && progress > 0,
                    Tick = tick, Nesting = Math.Max(0, deployment.Nesting - 1f / 60) };
            }
            _aimDeployment[id] = deployment;
            if (!_aimIntents.TryGetValue(id, out var intent) || intent.Life != state.LifeId || intent.Token != slot.Active.Token || intent.Selection != slot.SelectionRevision)
            { _aims.Remove(id); continue; }
            if (tick - intent.Received > 15) { _aims.Remove(id); continue; }
            var previous = _aims.GetValueOrDefault(id);
            // A rejected gameplay batch may be retried at the same boundary. Transient
            // articulation is a solution for that boundary, never a second elapsed tick.
            if (previous?.Tick == tick && previous.Pitch >= -Configuration.Aim.DownDegrees * MathF.PI / 180 &&
                previous.Pitch <= Configuration.Aim.UpDegrees * MathF.PI / 180) { continue; }
            var solution = WeaponAim.Solve(state, intent.Token, intent.Direction, previous, Configuration.Aim, tick,
                observations?.Single(request => request.VehicleId == id).Observation.Physics);
            _aims[id] = solution with { Ready = solution.Clear && !deployment.Retracting && deployment.Progress >= 1 && (slot.Active.Item != HeldItem.Missile || tick >= slot.MissileReadyTick) };
        }
    }

    /// <summary>Restores transient articulation if the enclosing authoritative batch fails.</summary>
    internal Action AimRollback()
    {
        var intents = _aimIntents.ToArray();
        var aims = _aims.ToArray();
        var deployment = _aimDeployment.ToArray();
        return () =>
        {
            ResetAims();
            foreach (var entry in intents) { _aimIntents.Add(entry.Key, entry.Value); }
            foreach (var entry in aims) { _aims.Add(entry.Key, entry.Value); }
            foreach (var entry in deployment) { _aimDeployment.Add(entry.Key, entry.Value); }
        };
    }

    private void ResetAim(ulong id)
    {
        _aims.Remove(id);
        _aimIntents.Remove(id);
    }

    private void ResetAims()
    {
        _aims.Clear();
        _aimIntents.Clear();
        _aimDeployment.Clear();
    }
}
