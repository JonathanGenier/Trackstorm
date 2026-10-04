using System.Net;
using System.Net.Sockets;
using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Items;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Real local UDP and production drivers exercise persistent item contracts and damaged rear-shield recovery.</summary>
public sealed partial class ShieldIntegrationChecks : Node
{
    private readonly List<GameNetworkingSocketsTransport> _gateways = new();
    private readonly List<VehicleNetworkDriver> _drivers = new();
    private readonly List<Hud.CombatHud> _huds = new();
    private readonly List<string> _evidence = new();
    private string _endpoint = string.Empty;
    private ulong[] _ids = [];
    private int _frames;
    private int _boundary;
    private int _stage;
    private bool _done;
    private static readonly DamageContext Hit = new("world", 0, "host-observed-runtime-hit");

    public override void _Ready()
    {
        Engine.PhysicsTicksPerSecond = 60;
        Engine.MaxFps = 60;
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        _endpoint = $"127.0.0.1:{((IPEndPoint)socket.Client.LocalEndPoint!).Port}";
        socket.Close();
        AddPeer(true);
        AddPeer(false);
    }

    private void AddPeer(bool host)
    {
        var gateway = new GameNetworkingSocketsTransport();
        ulong server = 0;
        if (host) { gateway.Listen(TransportEndpoint.DirectIp(_endpoint)); }
        else { server = gateway.Connect(TransportEndpoint.DirectIp(_endpoint)); }
        if (OS.GetCmdlineUserArgs().Contains("--shield-impaired")) { gateway.ConfigureSimulation(new(30, 5, 2, 0, 0)); }
        _gateways.Add(gateway);
        _drivers.Add(new(gateway, host ? 88ul : 0, server,
            configuration: host ? new() { Match = new() { MinimumPlayers = 1, CountdownTicks = 1 }, Damage = new() { MaxHP = 1500 } } : null));
        var driver = _drivers[^1];
        var hud = new Hud.CombatHud { Vehicle = () => driver.LocalState, Slot = () => driver.LocalItem,
            Shields = () => driver.ItemState?.Shields ?? Array.Empty<ShieldState>() };
        AddChild(hud);
        _huds.Add(hud);
    }

    public override void _PhysicsProcess(double delta)
    {
        _frames++;
        if (_done) { if (_frames - _boundary > 3) { GetTree().Quit(); } return; }
        try
        {
            foreach (var driver in _drivers)
            {
                driver.Advance(default, state => new(state.Movement.Physics, N.Vector3.UnitY));
                Check(driver.Failure.Length == 0, driver.Failure);
                if (driver.LocalState is { } local && driver.ItemState is { } accepted && driver.LocalItem is { } inventory)
                {
                    var nativeHud = _huds[_drivers.IndexOf(driver)];
                    nativeHud.Refresh();
                    var hud = nativeHud.Displayed!;
                    foreach (bool second in new[] { false, true })
                    {
                        var slot = second ? hud.SecondSlot : hud.FirstSlot;
                        ulong token = second ? inventory.SecondToken : inventory.Token;
                        var pool = accepted.Shields.FirstOrDefault(s => s.Attached && s.Owner == local.VehicleId && s.Life == local.LifeId && s.Token == token);
                        Check(slot.Resource?.Fraction == (slot.Item == HeldItem.Shield && pool is not null ? pool.HP / (double)ShieldState.DefaultHP : null),
                            "HUD follows each accepted physical pool through UDP damage, deployment, destruction and late admission.");
                    }
                }
            }
            Check(_frames - _boundary < 1800, $"Shield stage {_stage} timed out.");
            var host = _drivers[0].Host!;
            bool Converged() => _drivers.All(driver => driver.ItemState is { } items && items.Shields.SequenceEqual(host.Items.Shields) && items.Slots.SequenceEqual(host.Items.Slots));
            switch (_stage)
            {
                case 0 when _drivers.All(driver => driver.Latest?.Vehicles.Count == 2) && host.World.State.Match?.Phase == MatchPhase.Active:
                    foreach (ulong owner in new ulong[] { 1, 1, 2, 2 }) { Check(host.Items.Grant(host.World, owner, HeldItem.Shield), "Four ordinary grants."); }
                    _ids = host.Items.Shields.Select(state => state.Id).ToArray();
                    Next("Two UDP peers admitted; four independent Shields acquired through normal authority.");
                    break;
                case 1 when Converged():
                    Check(host.Items.Shields.All(state => state.HP == 1000), "Default HP is 1000.");
                    host.Items.DamageShield(host.World, _ids[0], 1, 325, Hit);
                    host.Items.DamageShield(host.World, _ids[1], 1, 100, Hit);
                    host.Items.DamageShield(host.World, _ids[2], 1, 50, Hit);
                    foreach (ulong id in new[] { _ids[0], _ids[2] })
                    {
                        Check(host.Items.Shields.Single(s => s.Id == id).Stage == ShieldStage.RearShield, "Selected acquisition automatically exposes shield.");
                        Check(!host.Items.TransitionShield(host.World, id, ShieldStage.Held, ShieldStage.RearShield), "Duplicate transition has no effect.");
                    }
                    Check(host.Items.DamageShield(host.World, _ids[0], 1, 325, Hit) is null, "Duplicate hit has no effect.");
                    var shield = host.Items.Shields[2];
                    byte[] request = ItemCodec.EncodeUse(88, shield.Life, shield.Token);
                    _gateways[1].Send(new(_gateways[1].Connections.Keys.Single(), request, TransportDelivery.Reliable));
                    _gateways[1].Send(new(_gateways[1].Connections.Keys.Single(), request, TransportDelivery.Reliable));
                    Next("Damage and duplicate hit/transition exercised; rear state retains slots and persistent pools.");
                    break;
                case 2 when Converged():
                    Check(host.Items.Shields.Select(state => state.HP).SequenceEqual(new float[] { 675, 900, 950, 1000 }), "Independent pool replication.");
                    foreach (ulong id in new[] { _ids[0] })
                    {
                        var pose = new VehiclePhysicsState(new((float)id * 5, 2, 30), N.Quaternion.Identity, N.Vector3.Zero, N.Vector3.Zero);
                        Check(host.Items.TransitionShield(host.World, id, ShieldStage.RearShield, ShieldStage.WorldWall, pose), "World wall contract.");
                        Check(!host.Items.TransitionShield(host.World, id, ShieldStage.RearShield, ShieldStage.WorldWall, pose), "Repeated world placement rejected.");
                    }
                    host.Items.DamageShield(host.World, _ids[0], 2, 125, Hit);
                    host.Items.DamageShield(host.World, _ids[2], 2, 50, Hit);
                    Check(host.World.State.Vehicles.All(vehicle => vehicle.Damage.CurrentHP == 1500), "Item damage never reduces vehicle health.");
                    Next("World transition releases its slot; the second damaged rear shield retains its slot. HP 550/900/900/1000, vehicles 1500 HP.");
                    break;
                case 3 when Converged():
                    var checkpoint = ResumeCheckpointCodec.Decode(ResumeCheckpointCodec.Encode(new(
                        new ItemPublication(1, host.Snapshot(), host.Items.Slots, [], [], shields: host.Items.Shields),
                        host.World.State.Match!, null, host.Configuration)));
                    var restored = HostVehicleSession.Restore(checkpoint, host.CaptureAuthority(), 1);
                    Check(restored.Items.Shields.SequenceEqual(host.Items.Shields), "Full authority checkpoint restores exact pools and stages.");
                    Check(restored.Items.DamageShield(restored.World, _ids[0], 2, 125, Hit) is null, "Restoration retains duplicate-hit watermark.");
                    AddPeer(false);
                    Next("Full checkpoint/authority restoration preserved pools, lifecycle, pose and replay memory; third UDP peer connecting.");
                    break;
                case 4 when _drivers.All(driver => driver.Latest?.Vehicles.Count == 3) && Converged():
                    Check(_drivers[2].ItemState!.Shields.Select(state => state.HP).SequenceEqual(new float[] { 550, 900, 900, 1000 }), "Late admission preserves damaged pools.");
                    Check(host.Items.DamageShield(host.World, _ids[0], 3, 5000, Hit) is { Amount: 550, DestroyedTransition: true }, "World wall lethal crossing exactly once.");
                    Check(host.Items.DamageShield(host.World, _ids[3], 1, 1000, Hit) is { DestroyedTransition: true }, "Attached lethal crossing frees only its slot.");
                    ulong revision = host.Items.ReliableRevision;
                    for (int i = 0; i < 20; i++)
                    {
                        Check(host.Items.DamageShield(host.World, _ids[0], (ulong)(3 + i), 5000, Hit) is null, "Destroyed wall cannot take damage again.");
                        Check(!host.Items.TransitionShield(host.World, _ids[3], ShieldStage.Held, ShieldStage.RearShield), "Destroyed item cannot redeploy.");
                    }
                    Check(host.Items.ReliableRevision == revision, "Repeated destruction does not republish.");
                    Next("Late join converged; lethal world/held damage and twenty repeated destruction attempts produced two removals.");
                    break;
                case 5 when Converged():
                    Check(host.Items.Shields.Select(state => state.Id).SequenceEqual(new[] { _ids[1], _ids[2] }), "Surviving pools unchanged across three peers.");
                    Check(host.World.State.Vehicles.All(vehicle => vehicle.Damage.CurrentHP == 1500), "Vehicles still undamaged.");
                    Check(host.World.Events.Entries.Count(entry => entry.Cause == "Shield" && entry.Kind == "Destroyed") == 2, "Exactly two committed destruction events.");
                    Next("Three UDP peers agree on remaining entities, stages, health and cleared slots; exactly two destruction outcomes and no vehicle damage.");
                    Check(_drivers[0].RequestItemSwitch(), "Select the surviving second physical pool.");
                    break;
                case 6 when Converged() && _drivers[0].LocalItem?.ActiveSlot == 1:
                    Check(_drivers[0].RequestItemDiscard() && _drivers[1].RequestItemDiscard(), "Ordinary host/remote discard requests.");
                    Next("Selection switched to the surviving pool without refill; ordinary discard requested on both peers.");
                    break;
                case 7 when Converged() && host.Items.Shields.Count == 0:
                    Check(_huds.Take(2).All(hud => hud.Displayed!.FirstSlot.Resource is null && hud.Displayed.SecondSlot.Resource is null), "Native HUD clears both discarded pools.");
                    Check(host.Items.Grant(host.World, 1, HeldItem.Shield) && host.Items.Grant(host.World, 1, HeldItem.Wrench), "Replacement grants.");
                    Next("Both native slot resources cleared after discard; replacement Shield acquired with a fresh pool.");
                    break;
                case 8 when Converged():
                    Check(_huds[0].Displayed!.FirstSlot.Resource?.Text == "1000" && _huds[0].Displayed!.SecondSlot.Resource is null, "Fresh shield HP and Wrench replacement stay independent.");
                    Check(host.Items.Shields.Single().Id > _ids.Max(), "Replacement never resurrects discarded identity.");
                    Next("Native HUD displays fresh replacement HP, independent non-resource second slot, and no stale discarded durability.");
                    Finish();
                    break;
            }
        }
        catch (Exception exception)
        {
            GD.PushError(exception.ToString());
            Cleanup();
            GetTree().Quit(1);
        }
    }

    private void Next(string message) { GD.Print(message); _evidence.Add(message); _stage++; _boundary = _frames; }
    private static void Check(bool condition, string message) { if (!condition) { throw new InvalidOperationException(message); } }
    private void Finish()
    {
        const string outputPrefix = "--shield-output=";
        string output = OS.GetCmdlineUserArgs().First(arg => arg.StartsWith(outputPrefix, StringComparison.Ordinal))[outputPrefix.Length..];
        System.IO.File.WriteAllLines(System.IO.Path.Combine(output, "evidence.txt"), _evidence);
        GD.Print("Shield integration passed: authoritative pools, lifecycle, repeated events, three-peer UDP and full state restoration.");
        Cleanup();
    }
    private void Cleanup()
    {
        _done = true;
        _boundary = _frames;
        foreach (var driver in _drivers) { driver.Dispose(); }
        foreach (var gateway in _gateways) { gateway.ConfigureSimulation(new()); gateway.Dispose(); }
    }
}
