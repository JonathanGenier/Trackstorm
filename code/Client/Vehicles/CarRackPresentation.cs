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
    private bool _suppressed;
    private bool _usePending;
    private HeldItem _useItem;
    private bool _engaged;
    private float _useTime;
    private ulong _life;

    internal HeldItem PresentedItem => _payload?.Visible == true ? _mounted : HeldItem.None;
    internal float Progress => _mechanism.Progress;

    public override void _Ready()
    {
        Node3D model = GetParent<Node3D>();
        _mechanism = model.GetChildren().OfType<CarDeployment>().Single();
        _rack = model.GetNode<Node3D>("WeaponRack");
    }

    /// <summary>Installs a fresh accepted publication, including remote inventories and recovery snapshots.</summary>
    internal void Observe(ulong life, bool alive, ItemSlot? inventory, IEnumerable<ItemEvent> events)
    {
        if (_life != life || !alive)
        {
            Reset();
            _life = life;
        }
        if (!alive) { return; }
        if (inventory?.Life != life) { inventory = null; }
        ItemSlot? active = inventory?.Active;
        var outcome = events.LastOrDefault(e => e.Item != HeldItem.Nitro && (e.Item == HeldItem.MachineGun || !e.Impact) &&
            (e.Token == active?.Token || e.Token == _previous?.Active.Token));
        bool used = outcome is not null;
        bool selection = inventory is not null && (_previous is null || inventory.ActiveSlot != _previous.ActiveSlot || inventory.SelectionRevision != _previous.SelectionRevision);
        bool acquired = active is not null && active.Item != HeldItem.None &&
            (_previous is null || active.Item != _previous.Active.Item || (active.Token != _previous.Active.Token && !used));
        if (selection || acquired)
        {
            _suppressed = false;
            _replace = _mounted != HeldItem.None;
            _usePending = false;
            _useTime = 0;
        }
        // Nitro now owns a chassis-mounted rear jet. Never deploy its obsolete rack placeholder.
        if (active?.Item == HeldItem.Nitro)
        {
            _desired = HeldItem.None;
            _usePending = false;
            _previous = inventory;
            return;
        }
        _desired = _suppressed ? HeldItem.None : active?.Item ?? HeldItem.None;
        _engaged = inventory is { EngagedToken: > 0 };
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
    }

    internal void Reset()
    {
        _mechanism.ResetPose();
        _payload?.QueueFree();
        _payload = null;
        _previous = null;
        _desired = _mounted = HeldItem.None;
        _replace = _suppressed = _usePending = _engaged = false;
        _useTime = 0;
    }

    public override void _Process(double delta)
    {
        if (_replace || (_desired != _mounted && _mounted != HeldItem.None))
        {
            _mechanism.Deployed = false;
            if (_mechanism.Progress <= 0)
            {
                _payload?.QueueFree();
                _payload = null;
                _mounted = HeldItem.None;
                _replace = false;
            }
        }
        else if (_mounted == HeldItem.None && _desired != HeldItem.None)
        {
            _mounted = _desired;
            _payload = Items.RackItemVisual.Create(_mounted);
            _payload.Visible = false;
            _rack.AddChild(_payload);
            _mechanism.Deployed = true;
        }
        else { _mechanism.Deployed = _desired != HeldItem.None; }

        if (_payload is not null)
        {
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
                    _suppressed = true;
                    _desired = HeldItem.None;
                }
            }
        }
    }
}
