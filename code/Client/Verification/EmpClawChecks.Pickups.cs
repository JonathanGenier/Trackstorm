using System.Net;
using System.Net.Sockets;
using Godot;
using Trackstorm.Client.Development;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Simulation;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

public sealed partial class EmpClawChecks
{
    private async Task CheckPickupAccess()
    {
        var arenas = new List<NetworkVehicleArena>();
        var gateways = new List<GameNetworkingSocketsTransport>();
        var views = new List<SubViewport>();
        var panel = new DeveloperOptionsPanel();
        AddChild(panel);
        AddChild(panel.Footer);
        foreach (string key in new[] { "spawns_emp_claw_weight", "items_emp_claw_deploy_seconds", "items_emp_claw_stow_seconds", "items_emp_claw_interval_ticks" })
        { Check(panel.FindChild(key, true, false) is LineEdit, "Production Developer Options exposes " + key); }
        panel.Footer.QueueFree(); panel.QueueFree();
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        string endpoint = $"127.0.0.1:{((IPEndPoint)socket.Client.LocalEndPoint!).Port}";
        socket.Close();
        try
        {
            for (int i = 0; i < 2; i++)
            {
                var gateway = new GameNetworkingSocketsTransport();
                ulong server = 0;
                if (i == 0) { gateway.Listen(TransportEndpoint.DirectIp(endpoint)); }
                else { server = gateway.Connect(TransportEndpoint.DirectIp(endpoint)); }
                gateways.Add(gateway);
                var view = new SubViewport { Size = new(1440, 900), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
                AddChild(view); views.Add(view);
                var arena = new NetworkVehicleArena { CameraInput = new Input.PlayerInputAdapter(new()) { CameraAvailable = true } };
                arena.Initialize(gateway, i == 0 ? 296ul : 0, server);
                view.AddChild(arena); arenas.Add(arena);
            }
            async Task Frame()
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                foreach (var arena in arenas)
                {
                    arena.Driver.DesiredAim = -N.Vector3.UnitZ;
                    arena.Advance(default);
                    if (arena.Driver.Failure.Length > 0) { throw new InvalidOperationException(arena.Driver.Failure); }
                }
            }
            async Task Until(Func<bool> test, string reason, int frames = 600)
            {
                for (int i = 0; i < frames && !test(); i++) { await Frame(); }
                Check(test(), reason);
            }
            await Until(() => arenas.All(a => a.Driver.Latest?.Vehicles.Count == 2), "Two real UDP peers connected to the production arena.");
            var host = arenas[0].Driver.Host!;
            var edits = ItemRegistry.All.ToDictionary(i => $"spawns.{i.Key}_weight", i => i.Identity == HeldItem.EmpClaw ? 1d : 0d);
            edits["spawns.cooldown_ticks"] = 30;
            edits["match.minimum_players"] = 1;
            edits["match.countdown_ticks"] = 1;
            Check(host.TryConfigure(host.Configuration.Revision, edits, out _), "Host accepts EMP-only weights through existing configuration transaction.");
            await Until(() => arenas.All(a => a.Driver.Configuration == host.Configuration), "EMP-only configuration replicates to both peers.");
            await Until(() => host.World.State.Match?.Phase == Core.Matches.MatchPhase.Active, "Match becomes active for normal item input.", 1200);
            void MoveToMarker()
            {
                var world = host.World;
                var position = arenas[0].MapConfiguration.Items[0].Position + N.Vector3.UnitY * .6f;
                var pose = new VehiclePhysicsState(position, N.Quaternion.Identity, N.Vector3.Zero, N.Vector3.Zero);
                world.Restore(new SimulationState(world.State.Tick, world.State.LastInput, world.State.Vehicles.Select(v => v.VehicleId == 1
                    ? new VehicleSnapshot(1, v.LifeId, new VehicleState(world.State.Tick, pose, false, false, 0, 0), v.Damage, pose) : v), world.State.Match));
            }
            bool Ready() => arenas.All(a => a.Driver.ItemState?.Slots.FirstOrDefault(s => s.Vehicle == 1) is { } inventory &&
                a.Bodies[1].Rack.IsEmpClawReady(inventory));
            MoveToMarker();
            await Until(() => host.Items.Slots.Any(s => s.Vehicle == 1 && s.Active.Item == HeldItem.EmpClaw), "Real marker overlap grants EMP Claw without a developer grant.");
            await Until(Ready, "Pickup deploys actual Blender launcher on owner and remote.");
            if (DisplayServer.GetName() != "headless")
            {
                arenas[0].GetNode<Camera3D>("ChaseCamera").Current = true;
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                views[0].GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath(Output + "/00-normal-pickup-camera.png"));
            }
            ulong magazine = host.Items.Slots.Single(s => s.Vehicle == 1).Active.EmpClaw!.Magazine;
            // Move clear of the marker to distinguish depletion from an automatic fresh pickup.
            var marker = arenas[0].MapConfiguration.Items[0];
            var current = host.World.State;
            host.World.Restore(new SimulationState(current.Tick, current.LastInput, current.Vehicles.Select(v =>
            {
                if (v.VehicleId != 1) { return v; }
                var p = new VehiclePhysicsState(marker.Position + new N.Vector3(0, .6f, -8), N.Quaternion.Identity, N.Vector3.Zero, N.Vector3.Zero);
                return new VehicleSnapshot(1, v.LifeId, new VehicleState(current.Tick, p, false, false, 0, 0), v.Damage, p);
            }), current.Match));
            var shotTicks = new List<ulong>();
            for (int i = 0; i < 3; i++)
            {
                await Until(() => Ready() && host.World.State.Tick >= host.Items.Slots.Single(s => s.Vehicle == 1).Active.EmpClaw!.ReadyTick,
                    "Round " + (i + 1) + " ready through normal presentation and authority gates.");
                Check(arenas[0].Driver.RequestItemUse(), "Normal use input accepts round " + (i + 1));
                int remaining = 2 - i;
                await Until(() => host.Items.Slots.Single(s => s.Vehicle == 1).Active.EmpClaw?.Remaining == remaining, "Accepted shot consumes exactly one visible round.");
                shotTicks.Add(host.World.State.Tick);
            }
            Check(shotTicks[2] - shotTicks[0] <= 120, $"Three normal input shots span {shotTicks[2] - shotTicks[0]} ticks ({(shotTicks[2] - shotTicks[0]) / 60f:F3}s); second shot after {shotTicks[1] - shotTicks[0]} ticks.");
            await Until(() => arenas.All(a => a.Bodies[1].Rack.EmpLauncher is null && a.Bodies[1].Rack.Progress == 0), "Final shot retracts cannon and closes trunk on both peers.");
            Check(!arenas[0].Driver.RequestItemUse(), "Normal input cannot fire a fourth shot from an empty magazine.");
            MoveToMarker();
            await Until(() => host.Items.Slots.Single(s => s.Vehicle == 1).Active.EmpClaw is { Remaining: 3 } load && load.Magazine != magazine, "Respawned existing marker supplies a fresh three-round magazine.");
            await Until(Ready, "Repeated normal pickup deploys reliably.");
            if (!host.Items.Slots.Single(s => s.Vehicle == 1).Full)
            { Check(arenas[0].Driver.GiveDeveloperItem(HeldItem.Wrench), "Existing developer grant supplies a switch target."); }
            for (int cycle = 0; cycle < 2; cycle++)
            {
                Check(arenas[0].Driver.RequestItemSwitch(), "Normal inventory switch accepted.");
                await Until(() => arenas.All(a => a.Driver.ItemState?.Slots.FirstOrDefault(s => s.Vehicle == 1) is { ActiveSlot: 1 } s &&
                    (s.Active.Item == HeldItem.EmpClaw ? a.Bodies[1].Rack.IsEmpClawReady(s) : a.Bodies[1].Rack.IsWrenchReady(s))),
                    "Second-slot weapon completes outgoing return and deployment on both peers.");
                Check(arenas[0].Driver.RequestItemSwitch(), "Return to first EMP magazine accepted.");
                await Until(() => host.Items.Slots.Single(s => s.Vehicle == 1).ActiveSlot == 0 && Ready(), "EMP redeploys after repeated normal weapon switches.");
                Check(host.Items.Slots.Single(s => s.Vehicle == 1).Active.EmpClaw!.Remaining == 3, "Switching never refills or spends stored rounds.");
            }
            Check(host.Items.Missiles.All(p => p.EmpClaw is not null), "EMP test shots use authoritative EMP flight without introducing another weapon.");
        }
        finally
        {
            foreach (var arena in arenas) { arena.QueueFree(); }
            for (int i = 0; i < 5; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
            foreach (var view in views) { view.QueueFree(); }
            foreach (var gateway in gateways) { gateway.Dispose(); }
            for (int i = 0; i < 5; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        }
    }
}
