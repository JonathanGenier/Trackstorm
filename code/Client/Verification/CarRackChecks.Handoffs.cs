using Godot;
using Trackstorm.Client.Items;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Verification;

public sealed partial class CarRackChecks
{
    private Action? _sampleHandoff;

    public override void _Process(double delta) => _sampleHandoff?.Invoke();

    private async Task CheckWeaponHandoffs()
    {
        // Sample after the production body, rack and payload have all animated.
        ProcessPriority = 1000;
        var host = _arenas[0].Driver.Host!;
        await SetupHandoff(HeldItem.Missile, HeldItem.Shield);
        await Until(() => HandoffReady(HeldItem.Missile), "Missile ready before regression reproduction");
        await SwitchHandoff(HeldItem.Shield, "missile-to-shield-ready", capture: true);
        await SwitchHandoff(HeldItem.Missile, "shield-to-missile-ready", capture: true);

        foreach (var item in ItemRegistry.All.Where(i => i.Identity != HeldItem.Shield))
        {
            foreach (bool midway in new[] { false, true })
            {
                await SetupHandoff(item.Identity, HeldItem.Shield);
                if (midway)
                {
                    await Until(() => _arenas.All(a => a.Bodies[Shooter].Rack.Progress > .05f), item.Key + " begins deploying");
                }
                else { await Until(() => HandoffReady(item.Identity), item.Key + " fully ready"); }
                string phase = midway ? "mid-deploy" : "ready";
                await SwitchHandoff(HeldItem.Shield, item.Key + "-to-shield-" + phase);
                await SwitchHandoff(item.Identity, "shield-to-" + item.Key + "-" + phase);
            }
            await SetupHandoff(HeldItem.Shield, item.Identity);
            await Until(() => _arenas.All(a => a.Bodies[Shooter].ShieldMountProgress > .05f), "Shield starts unfolding before " + item.Key);
            await SwitchHandoff(item.Identity, "shield-mid-unfold-to-" + item.Key);
            Action verifyRapid = MonitorHandoff(HeldItem.Shield, item.Key + " rapid switches");
            for (int i = 0; i < 7; i++)
            {
                Check(_arenas[1].Driver.RequestItemSwitch(), "Rapid selection " + i);
                await Frames(3);
            }
            await Until(() => HandoffReady(HeldItem.Shield), item.Key + " rapid switches settle on latest selection", 900);
            verifyRapid();
            Check(host.Items.Slots.Single(s => s.Vehicle == Shooter) is { Item: HeldItem.Shield } retained &&
                retained.SecondItem == item.Identity, item.Key + " rapid changes preserve both items");
        }
        Check(host.Items.Missiles.Count == 0 && _events.Count == 0, "Selection-only matrix never fires or consumes a weapon");
        foreach (int delay in new[] { 0, 12, 32, 46 })
        {
            await SetupHandoff(HeldItem.Missile, HeldItem.Wrench);
            await Until(() => HandoffReady(HeldItem.Missile), "Post-fire Missile ready");
            Check(_arenas[1].Driver.RequestItemUse(), "One Missile launch requested");
            await Until(() => host.Items.Slots.Single(s => s.Vehicle == Shooter).Item == HeldItem.None, "One round consumed");
            await Frames(delay);
            Action verify = MonitorHandoff(HeldItem.Shield, "post-fire pickup at tick " + delay);
            Check(host.Items.Grant(host.World, Shooter, HeldItem.Shield), "Shield pickup during post-fire return");
            await Until(() => HandoffReady(HeldItem.Shield), "Post-fire Shield ready", 900);
            verify();
            Check(host.Items.Slots.Single(s => s.Vehicle == Shooter).SecondItem == HeldItem.Wrench, "Post-fire pickup retains other slot");
        }
        await SetupHandoff(HeldItem.Missile, HeldItem.Shield);
        await Until(() => HandoffReady(HeldItem.Missile), "Early-use handoff ready");
        var shield = host.Items.Shields.Single(s => s.Owner == Shooter && s.Attached);
        int uses = _events.Count;
        Check(_arenas[1].Driver.RequestItemSwitch(), "Select Shield for queued early use");
        Check(_arenas[1].Driver.RequestItemUse() && _arenas[1].Driver.RequestItemUse(), "Early Shield taps coalesce");
        await Frames(12);
        Check(host.Items.Shields.Any(s => s.Id == shield.Id && s.Attached), "Early use does not bypass outgoing stow");
        await Until(() => host.Items.Shields.Any(s => s.Id == shield.Id && s.Stage == ShieldStage.WorldWall), "Queued Shield deploys after readiness", 900);
        await Frames(8);
        Check(_events.Count == uses + 1, "Queued Shield produces exactly one use");
        Check(host.Items.Slots.Single(s => s.Vehicle == Shooter).Item == HeldItem.Missile, "Queued Shield use retains unfired Missile");
        await SetupHandoff(HeldItem.Missile, HeldItem.Shield);
        await Until(() => HandoffReady(HeldItem.Missile), "Cancellation fixture ready");
        uses = _events.Count;
        Check(_arenas[1].Driver.RequestItemSwitch() && _arenas[1].Driver.RequestItemUse(), "Queue Shield during return");
        await Frames(3);
        Check(_arenas[1].Driver.RequestItemSwitch(), "Cancel queued Shield by selecting Missile");
        await Until(() => HandoffReady(HeldItem.Missile), "Latest Missile selection completes", 900);
        await Frames(40);
        Check(_events.Count == uses && host.Items.Slots.Single(s => s.Vehicle == Shooter) is
            { Item: HeldItem.Missile, SecondItem: HeldItem.Shield }, "Cancelled early use consumes neither weapon and fires nothing");
        foreach (var item in ItemRegistry.All.Where(i => i.Identity != HeldItem.Shield))
        {
            await SetupHandoff(item.Identity, HeldItem.Shield);
            await Until(() => HandoffReady(item.Identity), item.Key + " ready for post-use switching");
            Check(_arenas[1].Driver.RequestItemUse(), item.Key + " use before switch");
            await Frames(item.Sustained ? 40 : 2, item.Sustained ? InputButtons.UseItem : 0);
            await SwitchHandoff(HeldItem.Shield, item.Key + "-post-use-to-shield", allowConsumption: true);
            Check(host.Items.Slots.Single(s => s.Vehicle == Shooter).EngagedToken == 0, item.Key + " switch stops sustained engagement");
        }
        // Complete accepted state is reconstructed without historical one-shots.
        await SetupHandoff(HeldItem.Shield, HeldItem.Missile);
        await Until(() => HandoffReady(HeldItem.Shield), "Shield ready before presentation reseed");
        foreach (var arena in _arenas)
        {
            var state = arena.Driver.Latest!.Vehicles.Single(v => v.State.VehicleId == Shooter).State;
            arena.Bodies[Shooter].Reseed(state);
            arena.Bodies[Shooter].ObserveShields(state, arena.Driver.ItemState!.Shields);
            arena.Bodies[Shooter].Rack.Observe(state.LifeId, true, arena.Driver.ItemState.Slots.Single(s => s.Vehicle == Shooter), [], tick: arena.Driver.ItemState.World.Tick);
        }
        Action verifyReseed = MonitorHandoff(HeldItem.None, "Shield reconstruction");
        await Until(() => HandoffReady(HeldItem.Shield), "Reseed reconstructs selected Shield", 900);
        verifyReseed();
        await SwitchHandoff(HeldItem.Missile, "reconstructed-shield-to-missile");
        Check(host.TryConfigure(0, new Dictionary<string, double>
        { ["vehicle.trunk_deployment_speed"] = 1, ["vehicle.rack_deployment_speed"] = 1 }, out _), "Slow rack tuning accepted");
        await SetupHandoff(HeldItem.Shield, HeldItem.Nitro);
        await Until(() => HandoffReady(HeldItem.Shield), "Slow Shield deployment completes", 900);
        await SwitchHandoff(HeldItem.Nitro, "slow-shield-to-nitro", capture: true);
        await SwitchHandoff(HeldItem.Shield, "slow-nitro-to-shield");
        _gateways[0].ConfigureSimulation(new(30, 5, 2, 0, 0));
        await SetupHandoff(HeldItem.Missile, HeldItem.Shield);
        await Until(() => HandoffReady(HeldItem.Missile), "Impaired Missile ready");
        await SwitchHandoff(HeldItem.Shield, "impaired-missile-to-shield");
        await SwitchHandoff(HeldItem.Missile, "impaired-shield-to-missile");
        Check(_arenas[1].Driver.RequestItemSwitch(), "Select Shield before mid-stow reconstruction");
        await Until(() => _arenas.All(a => a.Driver.ItemState!.Slots.Single(s => s.Vehicle == Shooter).Active.Item == HeldItem.Shield), "Both peers retain pending Shield selection");
        foreach (var arena in _arenas)
        {
            var state = arena.Driver.Latest!.Vehicles.Single(v => v.State.VehicleId == Shooter).State;
            var publication = arena.Driver.ItemState!;
            arena.Bodies[Shooter].Reseed(state);
            arena.Bodies[Shooter].ObserveShields(state, publication.Shields);
            arena.Bodies[Shooter].Rack.Observe(state.LifeId, true, publication.Slots.Single(s => s.Vehicle == Shooter), [], tick: publication.World.Tick);
        }
        Action verifyRecovery = MonitorHandoff(HeldItem.Shield, "Mid-stow reconstruction under impairment");
        await Until(() => HandoffReady(HeldItem.Shield), "Pending Shield reconstructs after outgoing stow", 1200);
        verifyRecovery();
        _gateways[0].ConfigureSimulation(new());
    }

    private async Task SetupHandoff(HeldItem first, HeldItem second)
    {
        var host = _arenas[0].Driver.Host!;
        host.Items.RemovePlayer(Shooter);
        await Frames(4);
        await Until(() => _arenas.All(a => a.Bodies[Shooter].Rack.Progress == 0 && a.Bodies[Shooter].ShieldMountProgress == 0), "Previous rack completely closed");
        // Removal resets authority's selection watermark. Start a new fixture
        // life too, so a remote driver's old pending-command watermark cannot
        // masquerade as an unacknowledged selection in the next scenario.
        Position(1000, true);
        await Frames(4);
        Check(host.Items.Grant(host.World, Shooter, first), "Grant " + first);
        Check(host.Items.Grant(host.World, Shooter, second), "Retain second " + second);
    }

    private bool HandoffReady(HeldItem item) => AllPresent(item) &&
        (item != HeldItem.Shield || _arenas.All(a => a.Bodies[Shooter].ShieldMountProgress == 1));

    private async Task SwitchHandoff(HeldItem incoming, string scenario, bool capture = false, bool allowConsumption = false)
    {
        ItemSlot before = _arenas[0].Driver.Host!.Items.Slots.Single(s => s.Vehicle == Shooter);
        Action verify = MonitorHandoff(incoming, scenario);
        Check(_arenas[1].Driver.RequestItemSwitch(), scenario + " selection sent");
        if (incoming == HeldItem.Missile)
        { Check(!_arenas[1].Driver.RequestItemUse(), scenario + " rejects early Missile use without queuing"); }
        await Frames(12);
        if (capture) { await Capture(scenario + "-return"); }
        if (capture)
        {
            for (int frame = 0; frame < 16; frame++)
            {
                await Frames(8);
                await Capture(scenario + "-motion-" + frame.ToString("D2"));
            }
        }
        await Until(() => HandoffReady(incoming), scenario + " finishes on both peers", 720);
        if (capture) { await Capture(scenario + "-ready"); }
        verify();
        ItemSlot after = _arenas[0].Driver.Host!.Items.Slots.Single(s => s.Vehicle == Shooter);
        Check(after.Token == before.Token && after.SecondToken == before.SecondToken &&
            (after.Item == before.Item || allowConsumption && before.ActiveSlot == 0 && after.Item == HeldItem.None) &&
            (after.SecondItem == before.SecondItem || allowConsumption && before.ActiveSlot == 1 && after.SecondItem == HeldItem.None) && after.Active.Item == incoming,
            scenario + " preserves both capabilities and selected intent");
    }

    private Action MonitorHandoff(HeldItem incoming, string scenario)
    {
        bool[] closed = _arenas.Select(a => a.Bodies[Shooter].Rack.Progress == 0).ToArray();
        string? failure = null;
        _sampleHandoff = () =>
        {
            for (int peer = 0; peer < _arenas.Count; peer++)
            {
                var body = _arenas[peer].Bodies[Shooter];
                closed[peer] |= body.Rack.Progress == 0;
                if (body.ShieldMountProgress > 0 && body.Rack.ShieldCarrier is null)
                { failure ??= $"{scenario}: Shield unfolding without its carriage on peer {peer}, rack {body.Rack.Progress:F3}, Shield {body.ShieldMountProgress:F3}"; }
                if (body.ShieldMountProgress > 0 && body.Rack.Progress < .999f)
                { failure ??= $"{scenario}: Rack moving before Shield is nested on peer {peer}"; }
                if (incoming != HeldItem.None && body.Rack.PresentedItem == incoming && !closed[peer])
                { failure ??= $"{scenario}: Incoming {incoming} appeared before full trunk closure on peer {peer}"; }
            }
        };
        return () =>
        {
            _sampleHandoff = null;
            Check(failure is null, failure ?? scenario + " has exclusive mechanism ownership and full closure");
        };
    }
}
