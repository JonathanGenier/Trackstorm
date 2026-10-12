using Trackstorm.Core.Items;
using Godot;

namespace Trackstorm.Client.Vehicles;

internal sealed partial class CarRackPresentation
{
    private OilPour? _oilPour;
    private double _oilClock;
    private bool _oilFired;
    internal bool IsOilReady(ItemSlot inventory) => _payload is Items.OilJack { Extension: >= .999f } &&
        !_replace && !_oilFired && _previous?.Active.Token == inventory.Active.Token && _previous.SelectionRevision == inventory.SelectionRevision && _life == inventory.Life;
    internal ItemConfiguration OilConfiguration { get; set; } = new();
    internal void ObserveOil(IEnumerable<OilPatch> patches, ulong vehicle, ulong tick)
    {
        _oilClock = tick;
        _oilPour = patches.Where(p => p.Owner == vehicle && p.Pour is { } pour && pour.Life == _life).OrderByDescending(p => p.Pour!.StartTick).Select(p => p.Pour).FirstOrDefault();
        if (_oilPour is { Cancelled: false } && _oilClock < _oilPour.StartTick + (ulong)_oilPour.DurationTicks) { _oilFired = true; }
    }

    private bool AnimateOil(float delta)
    {
        _oilClock += delta * 60;
        bool firing = _oilPour is { Cancelled: false } && _oilClock < _oilPour.StartTick + (ulong)_oilPour.DurationTicks;
        if (_payload is not Items.OilJack && !(_mounted == HeldItem.None && (_desired == HeldItem.Oil || firing))) { return false; }
        if (_payload is not Items.OilJack)
        {
            _payload = new Items.OilJack(); _rack.AddChild(_payload); _mounted = HeldItem.Oil; _replace = false;
            // Admission/resume during a committed pour installs its active hardware;
            // replaying selection would leave projectiles emerging from a closed box.
            if (firing) { _mechanism.EnsureProgress(1); ((Items.OilJack)_payload).ReconstructPour(); }
        }
        var jack = (Items.OilJack)_payload;
        bool selected = _previous?.Active.Item == HeldItem.Oil && !_replace;
        bool expose = firing || (selected && !_oilFired);
        // The jester nests and the lid shuts before the shared rack lowers or changes payload.
        _mechanism.SetTimelineProgress(null);
        _mechanism.Deployed = expose || !jack.Nested;
        jack.Visible = _mechanism.Progress > .45f;
        jack.Position = new Vector3(0, .09f, 0);
        jack.Present(expose && _mechanism.Progress >= .999f, delta, firing ? _oilPour : null, OilConfiguration);
        if (!expose && jack.Nested && _mechanism.Progress <= 0)
        {
            ClearPayload(); _mounted = HeldItem.None; _replace = false; _usePending = false; _oilFired = false;
            _desired = _previous?.Active.Item ?? HeldItem.None;
        }
        return true;
    }
}
