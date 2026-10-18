using Godot;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Vehicles;

internal sealed partial class CarRackPresentation
{
    private ulong _wrenchCapability;
    private byte _wrenchSlot;
    private bool _wrenchOwnsRack;
    private bool _wrenchReturning;

    internal bool WrenchOwnsRack => _wrenchOwnsRack;

    // One clock owns both mechanisms. Small steps consume phase boundaries without
    // accelerating the shared rack independently or skipping the safe folded pose.
    private void AnimateWrenchMechanism(Items.WrenchArm arm, bool deploy, float delta)
    {
        while (delta > 0)
        {
            float step = Math.Min(delta, 1f / 120);
            delta -= step;
            bool moveRack = deploy ? Progress < 1 : arm.Nested;
            _mechanism.SetTimelineProgress(moveRack
                ? Mathf.MoveToward(Progress, deploy ? 1 : 0, step / arm.RackDuration(deploy)) : Progress);
            arm.PresentServices(_rack.Position.Y);
            arm.Present(deploy && Progress >= 1, step);
        }
    }

    /// <summary>The installed spent capability remains represented until its actual
    /// mechanism closes. Pending pickups remain authoritative and are never overwritten.</summary>
    internal ItemSlot? PresentedInventory(ItemSlot? inventory)
    {
        if (!_wrenchOwnsRack || inventory is null || inventory.Life != _life) { return inventory; }
        return _wrenchSlot == 0
            ? inventory with { Item = HeldItem.Wrench, Token = _wrenchCapability, Ammo = null, NitroCharge = 0, SalvoShots = 0 }
            : inventory with { SecondItem = HeldItem.Wrench, SecondToken = _wrenchCapability, SecondAmmo = null, SecondNitroCharge = 0, SecondSalvoShots = 0 };
    }

    private bool AnimateWrenchUse(float delta)
    {
        if (!_wrenchOwnsRack || _payload is not Items.WrenchArm arm) { return false; }
        Boost.Deploy = false;
        Boost.DeploymentTimeline = null;
        _replace = _usePending = false;
        _mechanism.Deployed = !_wrenchReturning || !arm.Nested;
        arm.Visible = true;
        arm.Scale = Vector3.One;
        arm.Position = Vector3.Zero;
        if (!_wrenchStarted && arm.IsReady)
        {
            arm.BeginRepair();
            _wrenchStarted = true;
        }
        AnimateWrenchMechanism(arm, !_wrenchReturning, delta);
        if (_wrenchStarted && !arm.Repairing) { _wrenchReturning = true; }
        if (_wrenchReturning && arm.Nested && Progress == 0)
        {
            ClearPayload();
            _mounted = HeldItem.None;
            _wrenchOwnsRack = _wrenchReturning = _wrenchStarted = false;
            _desired = _previous?.Active.Item ?? HeldItem.None;
        }
        return true;
    }
}
