using Trackstorm.Client.Items;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Vehicles;

internal sealed partial class CarRackPresentation
{
    private float _salvoProgress;
    private bool _salvoReturning;
    private ulong _salvoSelection;
    private double _salvoClock;
    private double _salvoBoundary;
    internal SalvoCatapult? Catapult => _payload as SalvoCatapult;
    internal bool IsSalvoReady(ItemSlot inventory) => Catapult is { IsReady: true } && !_salvoReturning && !_replace &&
        !inventory.Active.SalvoCommitted && _previous?.Active.Token == inventory.Active.Token && _previous.Life == inventory.Life;

    /// <summary>Ordinary world snapshots advance presentation between reliable ammunition events.</summary>
    internal void ObserveSalvoTick(ulong tick)
    {
        _salvoBoundary = Math.Max(_salvoBoundary, tick / 60d);
        _salvoClock = Math.Max(_salvoClock, _salvoBoundary);
    }

    private void ObserveSalvo(ItemSlot? inventory, ItemEvent? outcome, ulong tick)
    {
        ObserveSalvoTick(tick);
        if (inventory?.Active.SalvoCommitted == true && _previous?.Active.Item == HeldItem.Salvo &&
            inventory.ActiveSlot == _previous.ActiveSlot &&
            (inventory.Active.Token == _previous.Active.Token || _previous.Active.SalvoCommitted))
        { _salvoSelection = inventory.SelectionRevision; _replace = false; }
        if (Catapult is not { } catapult || _salvoReturning || inventory is null ||
            inventory.SelectionRevision != _salvoSelection) { return; }
        var current = inventory.Active;
        var prior = _previous?.Active;
        if (prior?.Item != HeldItem.Salvo || current.Item is not (HeldItem.Salvo or HeldItem.None)) { return; }
        bool released = current.SalvoShots < prior.SalvoShots &&
            (current.SalvoReadyTick > prior.SalvoReadyTick ||
                outcome is { Item: HeldItem.Salvo, Impact: false } && outcome.Token == prior.Token);
        catapult.ObserveRounds(current.SalvoShots, released);
        // Token rotation after each accepted round is continuation of this magazine.
        if (released && current.Item == HeldItem.Salvo) { _replace = false; }
        _usePending = false;
        _desired = current.Item;
    }

    private bool AnimateSalvo(float delta)
    {
        if (Catapult is null)
        {
            if (_desired != HeldItem.Salvo || _payload is not null || _mounted != HeldItem.None || Progress > 0) { return false; }
            var created = new SalvoCatapult();
            _rack.AddChild(created);
            _payload = created; _mounted = HeldItem.Salvo;
            _salvoSelection = _previous?.SelectionRevision ?? 0;
            created.ObserveRounds(_previous?.Active.SalvoShots ?? 0);
            _salvoProgress = 0; _salvoReturning = false; _replace = false;
            if (_previous?.Active.SalvoCommitted == true)
            {
                // A recovery boundary already owns a deployed firing mechanism;
                // reconstruct it without replaying old launches or a new deployment.
                _salvoProgress = 1;
                _mechanism.EnsureProgress(1);
            }
        }
        var catapult = Catapult!;
        _salvoClock = Math.Min(_salvoBoundary + .1, _salvoClock + delta);
        _salvoReturning |= _replace || _desired != HeldItem.Salvo || _previous?.SelectionRevision != _salvoSelection;
        _mechanism.SetTimelineProgress(null);
        Boost.Deploy = false; Boost.DeploymentTimeline = null;
        if (_salvoReturning)
        {
            // Center and finish release at full clearance before nesting either slide.
            catapult.Present(_salvoProgress, true, null, delta, Progress);
            if (catapult.Centered && !catapult.Cycling) { _salvoProgress = Math.Max(0, _salvoProgress - delta / .8f); }
            _mechanism.Deployed = _salvoProgress > 0;
        }
        else
        {
            _mechanism.Deployed = true;
            if (Progress >= .999f) { _salvoProgress = Math.Min(1, _salvoProgress + delta / ItemSlot.SalvoDeploymentSeconds); }
            if (_previous?.Active is { SalvoCommitted: true } shot)
            { catapult.PrepareRelease((float)(shot.SalvoReadyTick / 60d - _salvoClock)); }
        }
        catapult.Visible = Progress > .45f;
        catapult.Present(_salvoProgress, _salvoReturning, _aim, _salvoReturning ? 0 : delta, Progress);
        _usePending = false;
        if (_salvoReturning && _salvoProgress == 0 && Progress == 0)
        {
            ClearPayload(); _mounted = HeldItem.None; _replace = false; _salvoReturning = false;
        }
        return true;
    }
}
