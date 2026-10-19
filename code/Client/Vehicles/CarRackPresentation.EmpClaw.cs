using Trackstorm.Client.Items;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Vehicles;

internal sealed partial class CarRackPresentation
{
    private EmpClawRackState? _empState;
    private WeaponAimSolution? _empAim;
    private float _empProgress;
    private bool _empReturning;
    private ulong _empMountedToken;
    private float _empShotHold;
    private float _empDeploySeconds = 1.2f;
    private float _empStowSeconds = .82f;
    internal ItemConfiguration EmpConfiguration { get; set; } = new();

    internal EmpClawLauncher? EmpLauncher => _payload as EmpClawLauncher;

    internal bool IsEmpClawReady(ItemSlot inventory) => !_empReturning && EmpLauncher is { IsReady: true } &&
        _empState?.Life == inventory.Life && _empMountedToken == inventory.Active.EmpClaw?.Magazine &&
        _previous?.SelectionRevision == inventory.SelectionRevision;

    private void ObserveEmpInventory(ItemSlot? inventory, ulong tick)
    {
        var load = inventory?.Active.EmpClaw;
        EmpClawRackState? state = load is null ? null : new(inventory!.Life, load.Magazine, tick, true,
            (byte)((7 << (3 - load.Remaining)) & 7), (ulong)(3 - load.Remaining), load.Remaining < 3 ? 2 - load.Remaining : -1,
            EmpConfiguration.EmpClawIntervalTicks / 60f, load.ReadyTick > tick ? (load.ReadyTick - tick) / 60f : 0);
        ObserveEmpClaw(state, _empAim);
        if (inventory?.Active.Item == HeldItem.EmpClaw) { _desired = HeldItem.None; _usePending = false; }
    }

    /// <summary>Applies accepted presentation state. Null means deselection/removal, not a reload.</summary>
    internal void ObserveEmpClaw(EmpClawRackState? state, WeaponAimSolution? aim = null, bool restore = false)
    {
        state?.Validate();
        if (state is not null && state.Life != _life) { return; }
        if (!restore && state is not null && _empState is not null && state.Token == _empState.Token && state.Revision < _empState.Revision) { return; }
        if (restore)
        {
            if (_payload is EmpClawLauncher) { ClearPayload(); _mechanism.ResetPose(); }
            _empProgress = _empShotHold = 0;
            _empReturning = false;
        }
        _empAim = state is not null && aim?.Life == state.Life && aim.Token == state.Token ? aim : null;
        if (_payload is EmpClawLauncher launcher && state?.Token == _empMountedToken)
        {
            if (!restore && _empState is not null && state.ShotSequence > _empState.ShotSequence) { _empShotHold = .24f; }
            launcher.Observe(state, restore);
        }
        _empState = state;
        if (state is { Selected: true, RoundMask: > 0 } && _payload is not (null or EmpClawLauncher))
        {
            // Existing payload-specific return routines retain mechanism ownership.
            _replace = true;
            _desired = HeldItem.None;
        }
    }

    private bool AnimateEmpClaw(float delta)
    {
        bool requested = _empState is { Selected: true, RoundMask: > 0 };
        if (_payload is not EmpClawLauncher)
        {
            if (!requested || _payload is not null || _mounted != HeldItem.None || Progress > 0) { return false; }
            var created = new EmpClawLauncher();
            _rack.AddChild(created);
            _payload = created;
            _mounted = HeldItem.EmpClaw;
            _empMountedToken = _empState!.Token;
            _empDeploySeconds = EmpConfiguration.EmpClawDeploySeconds;
            _empStowSeconds = EmpConfiguration.EmpClawStowSeconds;
            created.Observe(_empState, restore: true);
            _empProgress = 0;
            _empReturning = false;
        }
        var launcher = (EmpClawLauncher)_payload;
        _empShotHold = Math.Max(0, _empShotHold - delta);
        _empReturning |= !requested || _empState?.Token != _empMountedToken;
        Boost.Deploy = false;
        Boost.DeploymentTimeline = null;
        _usePending = false;
        if (_empReturning)
        {
            // Center at full height first; never yaw through the open deck on return.
            launcher.Present(_empProgress, null, delta);
            if (_empShotHold <= 0 && launcher.Centered && !launcher.Reloading) { _empProgress = Math.Max(0, _empProgress - delta / _empStowSeconds); }
            launcher.Present(_empProgress, null, 0);
        }
        else
        {
            _empProgress = Math.Min(1, _empProgress + delta / _empDeploySeconds);
            launcher.Present(_empProgress, _empAim, delta);
        }
        _mechanism.SetTimelineProgress(Math.Min(1, _empProgress / .44f));
        if (_empReturning && _empProgress == 0)
        {
            ClearPayload();
            _mounted = HeldItem.None;
            _replace = false;
            _empReturning = false;
        }
        return true;
    }

    private void ResetEmpClaw()
    {
        _empState = null;
        _empAim = null;
        _empProgress = _empShotHold = 0;
        _empReturning = false;
        _empMountedToken = 0;
    }
}
