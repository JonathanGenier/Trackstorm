using Godot;
using Trackstorm.Client.Items;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Vehicles;

internal sealed partial class CarRackPresentation
{
    private float _missileProgress;
    private float _missileReturn;
    private bool _missileStowing;
    private ulong _missileReturnBoundary;
    private double _missileClock;
    private float _missileDeploySeconds = 1.6f;
    private float _missileStowSeconds = .9f;

    internal bool IsMissileReady(ItemSlot inventory) => _payload is MissileLauncher { Loaded: true } &&
        !_missileStowing && _missileProgress >= 1 && _missileClock >= inventory.MissileReadyTick &&
        _previous?.Life == inventory.Life && _previous.Active.Token == inventory.Active.Token &&
        _previous.SelectionRevision == inventory.SelectionRevision;

    private void ObserveMissile(ItemSlot? inventory, ulong tick)
    {
        _missileClock = tick;
        if (inventory is null) { return; }
        if (inventory.MissileReadyTick > inventory.MissileDeployStartTick)
        { _missileDeploySeconds = (inventory.MissileReadyTick - inventory.MissileDeployStartTick) / 60f; }
        if (inventory.MissileStowEndTick > inventory.MissileStowStartTick &&
            inventory.MissileStowEndTick != _missileReturnBoundary &&
            (_payload is MissileLauncher || tick < inventory.MissileStowEndTick))
        {
            _missileReturnBoundary = inventory.MissileStowEndTick;
            _missileStowSeconds = (inventory.MissileStowEndTick - inventory.MissileStowStartTick) / 60f;
            _missileReturn = 0;
            _missileStowing = true;
            if (_payload is MissileLauncher launcher)
            {
                // Depletion leaves an empty cradle; deselection folds the still-held round.
                ulong returningToken = _previous?.Active.Token ?? 0;
                launcher.Loaded = returningToken != 0 &&
                    ((inventory.Token == returningToken && inventory.Item == HeldItem.Missile) ||
                     (inventory.SecondToken == returningToken && inventory.SecondItem == HeldItem.Missile));
            }
        }
    }

    private bool AnimateMissile(float delta)
    {
        _missileClock += delta * 60;
        bool selected = _previous?.Active.Item == HeldItem.Missile;
        if (_payload is not MissileLauncher && !_missileStowing && !(selected && _mounted == HeldItem.None)) { return false; }
        if (_payload is not MissileLauncher)
        {
            if (_mounted != HeldItem.None) { return false; }
            var created = new MissileLauncher { Loaded = selected && !_missileStowing };
            _rack.AddChild(created);
            _payload = created;
            _mounted = HeldItem.Missile;
            _missileProgress = 0;
        }
        var launcher = (MissileLauncher)_payload;
        Boost.Deploy = false;
        Boost.DeploymentTimeline = null;
        _usePending = false;
        _replace = false;
        _desired = _previous?.Active.Item ?? HeldItem.None;
        if (_missileStowing || !selected)
        {
            _missileStowing = true;
            _missileReturn = Math.Min(1, _missileReturn + delta / Math.Max(.5f, _missileStowSeconds));
            float fold = 1 - MissileLauncher.Ease(_missileReturn, 0, .40f);
            launcher.Present(Math.Min(_missileProgress, fold), null, delta);
            // Hold the rack high until yaw/pitch are safely centered.
            float rack = launcher.Centered ? 1 - MissileLauncher.Ease(_missileReturn, .40f, 1) : 1;
            _mechanism.SetTimelineProgress(Math.Min(_mechanism.Progress, rack));
            if (_missileReturn >= 1 && launcher.Centered)
            {
                _mechanism.SetTimelineProgress(0);
                ClearPayload();
                _mounted = HeldItem.None;
                _missileStowing = false;
                _missileProgress = 0;
            }
            return true;
        }
        _missileProgress = Math.Min(1, _missileProgress + delta / Math.Max(1, _missileDeploySeconds));
        _mechanism.SetTimelineProgress(Mathf.Clamp(_missileProgress / .36f, 0, 1));
        launcher.Present(_missileProgress, _aim, delta);
        return true;
    }
}
