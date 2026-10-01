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

/// <summary>Real local UDP and production drivers exercise core contracts; physical deployment belongs to later Stories.</summary>
public sealed partial class TombstoneIntegrationChecks : Node
{
    private readonly List<GameNetworkingSocketsTransport> _gateways = new();
    private readonly List<VehicleNetworkDriver> _drivers = new();
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
        if (OS.GetCmdlineUserArgs().Contains("--tombstone-impaired")) { gateway.ConfigureSimulation(new(30, 5, 2, 0, 0)); }
        _gateways.Add(gateway);
        _drivers.Add(new(gateway, host ? 88ul : 0, server,
            configuration: host ? new() { Match = new() { MinimumPlayers = 1, CountdownTicks = 1 }, Damage = new() { MaxHP = 1500 } } : null));
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
            }
            Check(_frames - _boundary < 1800, $"Tombstone stage {_stage} timed out.");
            var host = _drivers[0].Host!;
            bool Converged() => _drivers.All(driver => driver.ItemState is { } items && items.Tombstones.SequenceEqual(host.Items.Tombstones) && items.Slots.SequenceEqual(host.Items.Slots));
            switch (_stage)
            {
                case 0 when _drivers.All(driver => driver.Latest?.Vehicles.Count == 2) && host.World.State.Match?.Phase == MatchPhase.Active:
                    foreach (ulong owner in new ulong[] { 1, 1, 2, 2 }) { Check(host.Items.Grant(host.World, owner, HeldItem.Tombstone), "Four ordinary grants."); }
                    _ids = host.Items.Tombstones.Select(state => state.Id).ToArray();
                    Next("Two UDP peers admitted; four independent Tombstones acquired through normal authority.");
                    break;
                case 1 when Converged():
                    Check(host.Items.Tombstones.All(state => state.HP == 1000), "Default HP is 1000.");
                    host.Items.DamageTombstone(host.World, _ids[0], 1, 325, Hit);
                    host.Items.DamageTombstone(host.World, _ids[1], 1, 100, Hit);
                    host.Items.DamageTombstone(host.World, _ids[2], 1, 50, Hit);
                    foreach (ulong id in new[] { _ids[0], _ids[2] })
                    {
                        Check(host.Items.TransitionTombstone(host.World, id, TombstoneStage.Held, TombstoneStage.RearShield), "Rear shield contract.");
                        Check(!host.Items.TransitionTombstone(host.World, id, TombstoneStage.Held, TombstoneStage.RearShield), "Duplicate transition has no effect.");
                    }
                    Check(host.Items.DamageTombstone(host.World, _ids[0], 1, 325, Hit) is null, "Duplicate hit has no effect.");
                    var stone = host.Items.Tombstones[2];
                    byte[] request = ItemCodec.EncodeUse(88, stone.Life, stone.Token);
                    _gateways[1].Send(new(_gateways[1].Connections.Keys.Single(), request, TransportDelivery.Reliable));
                    _gateways[1].Send(new(_gateways[1].Connections.Keys.Single(), request, TransportDelivery.Reliable));
                    Next("Damage and duplicate hit/transition exercised; rear state retains slots and persistent pools.");
                    break;
                case 2 when Converged():
                    Check(host.Items.Tombstones.Select(state => state.HP).SequenceEqual(new float[] { 675, 900, 950, 1000 }), "Independent pool replication.");
                    foreach (ulong id in new[] { _ids[0], _ids[2] })
                    {
                        var pose = new VehiclePhysicsState(new((float)id * 5, 2, 30), N.Quaternion.Identity, N.Vector3.Zero, N.Vector3.Zero);
                        Check(host.Items.TransitionTombstone(host.World, id, TombstoneStage.RearShield, TombstoneStage.WorldWall, pose), "World wall contract.");
                        Check(!host.Items.TransitionTombstone(host.World, id, TombstoneStage.RearShield, TombstoneStage.WorldWall, pose), "Repeated world placement rejected.");
                    }
                    host.Items.DamageTombstone(host.World, _ids[0], 2, 125, Hit);
                    host.Items.DamageTombstone(host.World, _ids[2], 2, 50, Hit);
                    Check(host.World.State.Vehicles.All(vehicle => vehicle.Damage.CurrentHP == 1500), "Item damage never reduces vehicle health.");
                    Next("World transitions release only their own slots; HP remains 550/900/900/1000, vehicles remain 1500 HP.");
                    break;
                case 3 when Converged():
                    var checkpoint = ResumeCheckpointCodec.Decode(ResumeCheckpointCodec.Encode(new(
                        new ItemPublication(1, host.Snapshot(), host.Items.Slots, [], [], tombstones: host.Items.Tombstones),
                        host.World.State.Match!, null, host.Configuration)));
                    var restored = HostVehicleSession.Restore(checkpoint, host.CaptureAuthority(), 1);
                    Check(restored.Items.Tombstones.SequenceEqual(host.Items.Tombstones), "Full authority checkpoint restores exact pools and stages.");
                    Check(restored.Items.DamageTombstone(restored.World, _ids[0], 2, 125, Hit) is null, "Restoration retains duplicate-hit watermark.");
                    AddPeer(false);
                    Next("Full checkpoint/authority restoration preserved pools, lifecycle, pose and replay memory; third UDP peer connecting.");
                    break;
                case 4 when _drivers.All(driver => driver.Latest?.Vehicles.Count == 3) && Converged():
                    Check(_drivers[2].ItemState!.Tombstones.Select(state => state.HP).SequenceEqual(new float[] { 550, 900, 900, 1000 }), "Late admission preserves damaged pools.");
                    Check(host.Items.DamageTombstone(host.World, _ids[0], 3, 5000, Hit) is { Amount: 550, DestroyedTransition: true }, "World wall lethal crossing exactly once.");
                    Check(host.Items.DamageTombstone(host.World, _ids[3], 1, 1000, Hit) is { DestroyedTransition: true }, "Attached lethal crossing frees only its slot.");
                    ulong revision = host.Items.ReliableRevision;
                    for (int i = 0; i < 20; i++)
                    {
                        Check(host.Items.DamageTombstone(host.World, _ids[0], (ulong)(3 + i), 5000, Hit) is null, "Destroyed wall cannot take damage again.");
                        Check(!host.Items.TransitionTombstone(host.World, _ids[3], TombstoneStage.Held, TombstoneStage.RearShield), "Destroyed item cannot redeploy.");
                    }
                    Check(host.Items.ReliableRevision == revision, "Repeated destruction does not republish.");
                    Next("Late join converged; lethal world/held damage and twenty repeated destruction attempts produced two removals.");
                    break;
                case 5 when Converged():
                    Check(host.Items.Tombstones.Select(state => state.Id).SequenceEqual(new[] { _ids[1], _ids[2] }), "Surviving pools unchanged across three peers.");
                    Check(host.World.State.Vehicles.All(vehicle => vehicle.Damage.CurrentHP == 1500), "Vehicles still undamaged.");
                    Check(host.World.Events.Entries.Count(entry => entry.Cause == "Tombstone" && entry.Kind == "Destroyed") == 2, "Exactly two committed destruction events.");
                    Next("Three UDP peers agree on remaining entities, stages, health and cleared slots; exactly two destruction outcomes and no vehicle damage.");
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
        string output = OS.GetCmdlineUserArgs().First(arg => arg.StartsWith("--tombstone-output=", StringComparison.Ordinal))[19..];
        System.IO.File.WriteAllLines(System.IO.Path.Combine(output, "evidence.txt"), _evidence);
        GD.Print("Tombstone integration passed: authoritative pools, lifecycle, repeated events, three-peer UDP and full state restoration.");
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
