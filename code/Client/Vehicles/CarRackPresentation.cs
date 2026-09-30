using Godot;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Vehicles;

/// <summary>Reconstructable rack animation from confirmed inventory/outcomes; never authors item state.</summary>
internal sealed partial class CarRackPresentation : Node
{
    private CarDeployment _mechanism = null!;
    private Node3D _rack = null!;
    private Node3D? _payload;
    private ItemSlot? _previous;
    private HeldItem _desired;
    private HeldItem _mounted;
    private bool _replace;
    private bool _usePending;
    private HeldItem _useItem;
    private bool _engaged;
    private float _useTime;
    private ulong _life;
    private float _nitroRemaining;
    private float _replacementStart;
    private float _replacementNozzle;
    private ulong _nitroReadyTick;
    private ProxyMineState? _placement;
    private bool _mineReturning;

    internal required BoostExhaust Boost { get; init; }

    internal HeldItem PresentedItem => _payload?.Visible == true ? _mounted : HeldItem.None;
    internal float Progress => _mechanism.Progress;

    public override void _Ready()
    {
        Node3D model = GetParent<Node3D>();
        _mechanism = model.GetChildren().OfType<CarDeployment>().Single();
        _rack = model.GetNode<Node3D>("WeaponRack");
        Boost.Visible = false;
        _rack.AddChild(Boost);
    }

    /// <summary>Installs a fresh accepted publication, including remote inventories and recovery snapshots.</summary>
    internal void Observe(ulong life, bool alive, ItemSlot? inventory, IEnumerable<ItemEvent> events, ProxyMineState? placement = null, ulong tick = 0)
    {
        if (_life != life || !alive)
        {
            Reset();
            _life = life;
        }
        if (!alive) { return; }
        // A newly accepted placement supersedes an unfinished empty-arm return.
        _mineReturning = placement is null && (_mineReturning || _placement is not null);
        _placement = placement;
        if (_payload is Items.ProxyMineRack arm) { arm.Observe(placement); }
        if (inventory?.Life != life) { inventory = null; }
        ItemSlot? active = inventory?.Active;
        var outcome = events.LastOrDefault(e => e.Item != HeldItem.Nitro && (e.Item == HeldItem.MachineGun || !e.Impact) &&
            (e.Token == active?.Token || e.Token == _previous?.Active.Token));
        bool used = outcome is not null;
        bool selection = inventory is not null && (_previous is null || inventory.ActiveSlot != _previous.ActiveSlot || inventory.SelectionRevision != _previous.SelectionRevision);
        // The same selected capability becoming empty is confirmed depletion. A missing
        // publication, item switch, death or reseed must never replay this terminal cue.
        if (!selection && _previous?.Active is { Item: HeldItem.Nitro } prior &&
            active is { Item: HeldItem.None, NitroCharge: 0 } && active.Token == prior.Token)
        {
            Boost.Exhausted();
        }
        bool acquired = active is not null && active.Item != HeldItem.None &&
            (_previous is null || active.Item != _previous.Active.Item || (active.Token != _previous.Active.Token && !used));
        if (selection || acquired)
        {
            _replacementStart = _mechanism.Progress;
            _replacementNozzle = Boost.Deployment;
            _replace = _mounted != HeldItem.None;
            _usePending = false;
            _useTime = 0;
        }
        // Authority clears depleted slots. Cooldown and trigger release do not empty them.
        _desired = active?.Item ?? HeldItem.None;
        _engaged = active?.Item != HeldItem.Nitro && inventory is { EngagedToken: > 0 };
        if (outcome is not null || (_previous is null && _engaged))
        {
            _useItem = outcome?.Item ?? active!.Item;
            _usePending = true;
            _useTime = 0.32f;
            if (_mounted != HeldItem.None && _mounted != _useItem) { _replace = true; }
        }
        // Even an immediate switch+use must display the consumed selected item,
        // never borrow the old model while its replacement is retracting.
        if (_usePending) { _desired = _useItem; }
        _previous = inventory;
        if (_placement is not null || _mineReturning)
        {
            _desired = HeldItem.ProxyMine;
            _usePending = false;
            _replace = _mounted is not (HeldItem.None or HeldItem.ProxyMine);
        }
        if (_desired == HeldItem.Nitro)
        {
            float confirmedRemaining = inventory!.NitroDeploymentTicks / 60f;
            _nitroReadyTick = checked(tick + (ulong)inventory.NitroDeploymentTicks);
            _nitroRemaining = selection || acquired ? confirmedRemaining : Math.Min(_nitroRemaining, confirmedRemaining);
            AnimateNitroDeployment(0);
        }
    }

    internal void Reset()
    {
        _mechanism.ResetPose();
        ClearPayload();
        Boost.Reset();
        Boost.DeploymentTimeline = null;
        _previous = null;
        _desired = _mounted = HeldItem.None;
        _replace = _usePending = _engaged = false;
        _useTime = 0;
        _placement = null;
        _mineReturning = false;
    }

    public override void _Process(double delta)
    {
        if (_desired == HeldItem.Nitro)
        {
            AnimateNitroDeployment(Math.Max(0, (float)delta));
            return;
        }
        _mechanism.SetTimelineProgress(null);
        Boost.DeploymentTimeline = null;
        Boost.Deploy = _mounted == HeldItem.Nitro && _desired == HeldItem.Nitro && !_replace && _mechanism.Progress >= .999f;
        if (_replace || (_desired != _mounted && _mounted != HeldItem.None))
        {
            // Nest the barrel before lowering the rack through the open deck.
            _mechanism.Deployed = _mounted == HeldItem.Nitro && Boost.Deployment > 0;
            if (_mechanism.Progress <= 0)
            {
                ClearPayload();
                _mounted = HeldItem.None;
                _replace = false;
            }
        }
        else if (_mounted == HeldItem.None && _desired != HeldItem.None)
        {
            _mounted = _desired;
            _payload = _mounted == HeldItem.Nitro ? Boost : Items.RackItemVisual.Create(_mounted);
            _payload.Visible = false;
            if (_mounted != HeldItem.Nitro) { _rack.AddChild(_payload); }
            if (_payload is Items.ProxyMineRack arm) { arm.Observe(_placement); }
            _mechanism.Deployed = true;
        }
        else { _mechanism.Deployed = _desired != HeldItem.None; }

        if (_payload is not null)
        {
            if (_mounted == HeldItem.Nitro)
            {
                // The underslung chamber clears the bay floor before becoming visible.
                _payload.Visible = _mechanism.Progress > .72f;
                return;
            }
            if (_payload is Items.ProxyMineRack mine)
            {
                if (_placement is not null)
                {
                    _mechanism.EnsureProgress((1 - (float)_placement.PlacementTicks / ProxyMineState.PlacementDurationTicks) / .30f);
                }
                mine.ShowStored = _previous?.Active.Item == HeldItem.ProxyMine;
                _payload.Position = Vector3.Zero;
                _payload.Visible = _mechanism.Progress > .92f;
                if (_placement is null && _mineReturning && !mine.Returning)
                {
                    _mineReturning = false;
                    _desired = _previous?.Active.Item ?? HeldItem.None;
                }
                return;
            }
            // Payload appears only above the compartment; scale in/out above clear height.
            float reveal = Mathf.SmoothStep(0, 1, Mathf.Clamp((_mechanism.Progress - 0.92f) / 0.08f, 0, 1));
            _payload.Visible = reveal > 0;
            _payload.Scale = Vector3.One * Math.Max(0.001f, reveal);
            _payload.Position = new Vector3(0, 0.17f, 0);
            if (_usePending && _mounted == _useItem && !_replace && _mechanism.Progress >= 0.999f)
            {
                _useTime = Math.Max(0, _useTime - (float)delta);
                float pulse = MathF.Sin(_useTime * 45) * 0.025f;
                _payload.Position += new Vector3(0, pulse, pulse);
                if (_useTime == 0 && !_engaged)
                {
                    _usePending = false;
                    _desired = _previous?.Active.Item ?? HeldItem.None;
                }
            }
        }
    }

    private void AnimateNitroDeployment(float delta)
    {
        _nitroRemaining = Math.Max(0, _nitroRemaining - delta);
        if (_previous is { EngagedToken: > 0 } inventory && inventory.EngagedToken == inventory.Active.Token &&
            Boost.Source()?.Movement is { Nitro.Active: true } movement && movement.Tick >= _nitroReadyTick) { _nitroRemaining = 0; }
        float elapsed = ItemSlot.NitroDeploymentDurationTicks / 60f - _nitroRemaining;
        if (elapsed < .12f)
        {
            _mechanism.SetTimelineProgress(_replacementStart * (1 - Mathf.Clamp((elapsed - .05f) / .07f, 0, 1)));
            Boost.Deploy = false;
            Boost.DeploymentTimeline = _replacementNozzle * Math.Max(0, 1 - elapsed / .05f);
            return;
        }
        if (_mounted != HeldItem.Nitro || _replace)
        {
            ClearPayload();
            Boost.Reset();
            _mounted = HeldItem.Nitro;
            _payload = Boost;
            _replace = false;
        }
        _mechanism.SetTimelineProgress(Mathf.Clamp((elapsed - .12f) / .32f, 0, 1));
        Boost.DeploymentTimeline = _nitroRemaining <= 0 ? 1 : Mathf.Clamp((elapsed - .44f) / .16f, 0, 1);
        Boost.Deploy = _nitroRemaining <= 0;
        Boost.Visible = _mechanism.Progress > .72f;
    }

    private void ClearPayload()
    {
        if (_payload == Boost) { Boost.Visible = false; }
        else { _payload?.QueueFree(); }
        _payload = null;
    }
}
