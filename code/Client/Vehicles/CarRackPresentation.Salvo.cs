using Trackstorm.Client.Items;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Vehicles;

internal sealed partial class CarRackPresentation
{
    private float _salvoProgress;
    private bool _salvoReturning;
    private ulong _salvoSelection;
    internal SalvoCatapult? Catapult => _payload as SalvoCatapult;

    private void ObserveSalvo(ItemSlot? inventory, ItemEvent? outcome)
    {
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
        }
        var catapult = Catapult!;
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
            if (Progress >= .999f) { _salvoProgress = Math.Min(1, _salvoProgress + delta / 1.25f); }
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
