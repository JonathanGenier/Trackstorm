using Godot;
using Trackstorm.Core.Arenas;
using Trackstorm.Core.Items;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

public sealed partial class WeaponAimChecks
{
    private async Task CheckSalvoAim()
    {
        var host = _arenas[0].Driver.Host!;
        var driver = _arenas[1].Driver;
        var launches = new Dictionary<ulong, (ulong Tick, SalvoFlight Arc)>();
        var events = new List<ItemEvent>(); var remote = new List<ItemEvent>();
        var weights = ItemRegistry.All.ToDictionary(item => $"spawns.{item.Key}_weight", item => item.Identity == HeldItem.Salvo ? 1d : 0d);
        weights["spawns.category_weapon_weight"] = 100;
        Require(host.TryConfigure(0, weights, out _), "Developer spawn controls isolate Salvo with Weapon category enabled");
        var native = _arenas[0].Driver.CollideMissile!;
        _arenas[0].Driver.CollideMissile = (missile, end) =>
        {
            if (missile.Arc is { } arc)
            {
                if (launches.TryGetValue(missile.Id, out var original) && original.Arc.Target != arc.Target)
                { throw new InvalidOperationException("Later camera motion steered a launched Salvo round."); }
                launches.TryAdd(missile.Id, (host.World.State.Tick + 1, arc));
            }
            return native(missile, end);
        };
        _arenas[0].Driver.ItemsReceived += p => events.AddRange(p.Events.Where(e => e.Item == HeldItem.Salvo));
        _arenas[2].Driver.ItemsReceived += p => remote.AddRange(p.Events.Where(e => e.Item == HeldItem.Salvo));
        _heldShooter = new(0, Ground, 35);
        _heldTargets = (new(-12, Ground, 0), new(12, Ground, 0));
        Position(_heldShooter.Value, _heldTargets.Value.First, _heldTargets.Value.Second);
        await Frames(60);

        async Task Equip()
        {
            host.Items.RemovePlayer(Shooter);
            await Until(() => _arenas.All(a => a.Bodies[Shooter].Rack.Progress == 0), "Prior Salvo closes trunk");
            var map = _arenas[0].MapConfiguration;
            var marker = new ArenaSpawn("salvo-pickup", host.World.GetVehicle(Shooter).ObservedPhysics.Position, 0);
            var layout = new ArenaConfiguration(map.Minimum, map.Maximum, map.Players, [marker], map.Surfaces);
            var pickups = new ItemSpawnAuthority(layout, host.Items, new ItemSelectionRandom(211), host.Configuration.Configuration.Spawns);
            pickups.Advance(host.World);
            Require(pickups.TryPickup(host.World, marker.Id, Shooter) && !pickups.TryPickup(host.World, marker.Id, Shooter), "Normal weighted pickup awards one five-round Salvo exactly once");
            await Frames(5);
            Require(!driver.RequestItemUse(), "Local deployment rejects an early trigger");
            await Until(() => driver.LocalItem is { } item && driver.SalvoReady?.Invoke(item) == true && host.Items.SalvoReady(host.Items.Slots.Single(s => s.Vehicle == Shooter)), "Catapult completes captive preload on authority and owner");
            await Frames(30);
            Require(_arenas.All(a => a.Bodies[Shooter].Rack.Catapult is { VisibleRounds: 5, FuseLit: true }), "All peers show five secured rounds and a lit loaded fuse");
        }

        if (OS.GetCmdlineUserArgs().Contains("--salvo-death-only"))
        { await Death(); _arenas[0].Driver.CollideMissile = native; return; }

        await Equip();
        int priorLaunches = launches.Count;
        await Frames(180);
        Require(launches.Count == priorLaunches && host.Items.Slots.Single(s => s.Vehicle == Shooter).Active.SalvoReadyTick == 0,
            "Lit fuse has no timer and does not launch by itself");
        foreach (float degrees in new[] { 0f, 90f, 180f, -90f })
        {
            Vector3 horizontal = new(-MathF.Sin(Mathf.DegToRad(degrees)), 0, -MathF.Cos(Mathf.DegToRad(degrees)));
            await AimAt(_arenas[1].Bodies[Shooter].VisualPosition + horizontal * 55 + Vector3.Up * .2f);
            await Frames(50);
            var aim = host.Items.Aims.Single(a => a.Vehicle == Shooter);
            Require(Math.Abs(Mathf.AngleDifference(aim.Yaw, Mathf.DegToRad(degrees))) < .08f, "Camera reaches Salvo bearing " + degrees);
            Require(_arenas.All(a => a.Bodies[Shooter].Rack.Catapult is { } catapult && Math.Abs(Mathf.AngleDifference(catapult.Yaw, aim.Yaw)) < .08f), "All peers articulate the same Salvo bearing " + degrees);
            Require(_arenas[1].SalvoMarker.Visible && _arenas[1].SalvoMarker.RingCount == 1, "Existing ground circle covers bearing " + degrees);
            Require(_arenas[1].SalvoMarker.SurfaceVertices.Chunk(3).All(face => face.Max(v => v.Y) - face.Min(v => v.Y) <= 3.01f),
                "Ground circle never bridges wall/ground discontinuities at bearing " + degrees);
            Require(_arenas.Where(a => a != _arenas[1]).All(a => !a.SalvoMarker.Visible), "Remote inventories do not draw owner targeting circles");
            await Capture("salvo-bearing-" + degrees);
        }

        await AimAtCurrent(() => _arenas[1].Bodies[1].VisualTransform * new Vector3(0, .3f, 0));
        await Frames(50);
        Require(_arenas[1].AssistedCar == 1 && _arenas[1].TargetIndicator.Target == 1, "Salvo acquires the shared assisted-car marker");
        await Capture("salvo-shared-assist");
        Require(driver.RequestItemUse(), "One remote activation commits five shots");
        await Until(() => launches.Count > priorLaunches, "First assisted shot launches");
        Require(!driver.RequestItemUse() && !driver.RequestItemSwitch(), "Repeated trigger and switching cannot cancel or duplicate committed shots");
        Require(_arenas[1].AssistedCar == 1, "Shot-token rotation preserves shared assistance");
        // Actual camera input changes each subsequent launch; old arcs are checked
        // on every native swept segment above, including after target departure.
        foreach (var offset in new[] { new Vector3(70, 0, 0), new Vector3(0, 0, 70), new Vector3(-70, 15, 0), new Vector3(0, 65, -40) })
        { await AimAt(_arenas[1].Bodies[Shooter].VisualPosition + offset); }
        await Until(() => driver.LocalItem?.Active.Item == HeldItem.None, "Committed magazine depletes after camera changes");
        var volley = launches.Values.Skip(priorLaunches).ToArray();
        Require(volley.Length == 5 && volley.Zip(volley.Skip(1)).All(p => p.Second.Tick - p.First.Tick == 75), "All five launches use the exact configured interval");
        Require(volley.Select(v => v.Arc.Target).Distinct().Count() >= 4, "Later camera directions independently change successive targets");
        await Frames(90);
        Require(events.SequenceEqual(remote), "Observer receives identical ordered launch/impact outcomes");

        // Fire completely outside native ground support. Even losing the aim
        // stream does not pause the committed controller.
        await Equip();
        _heldShooter = new(800, 45, 800);
        Position(_heldShooter.Value, _heldTargets.Value.First, _heldTargets.Value.Second);
        await AimAt(new(900, 90, 850));
        priorLaunches = launches.Count;
        Require(driver.RequestItemUse(), "Out-of-arena activation accepted");
        await Until(() => launches.Count > priorLaunches, "Out-of-arena first launch");
        _wires[0].ConfigureSimulation(new(30, 5, 2, 0, 0));
        await Until(() => launches.Count == priorLaunches + 5, "Out-of-arena volley completes through impaired transport");
        await Frames(100);
        Require(driver.LocalItem?.Active.Item == HeldItem.None && events.SequenceEqual(remote), "Impaired out-of-arena depletion and outcomes converge");
        await Capture("salvo-outside-arena");
        var outside = launches.Values.Skip(priorLaunches).Select(shot => shot.Arc).ToArray();
        Require(outside.Length == 5 && host.Items.Missiles.Count(m => m.Arc is not null) >= 5,
            "No-contact rounds remain active beyond their aimed endpoints");
        await Until(() => host.Items.Missiles.All(m => m.Arc is null), "Out-of-arena rounds expire through authority after thirty seconds", 1900);
        await Frames(30);
        Require(events.SequenceEqual(remote), "Expiry outcomes converge once through impaired native UDP");

        _wires[0].ConfigureSimulation(new());
        _heldShooter = new(0, Ground, 35);
        Position(_heldShooter.Value, _heldTargets.Value.First, _heldTargets.Value.Second);
        await AimAt(new(50, Ground + 20, 5));
        int? stableNodes = null;
        ulong soakStart = host.World.State.Tick;
        for (int cycle = 0; cycle < 8; cycle++)
        {
            await Equip();
            priorLaunches = launches.Count;
            Require(driver.RequestItemUse(), "Repeated pickup activation " + cycle);
            await Until(() => driver.LocalItem?.Active.Item == HeldItem.None, "Repeated volley depletion " + cycle);
            await Until(() => host.Items.Missiles.Count == 0 && _arenas.All(a => a.Bodies[Shooter].Rack.Progress == 0), "Repeated projectiles and rack cleanup " + cycle, 1900);
            await Frames(120);
            int nodes = CountNodes(this);
            stableNodes ??= nodes;
            Require(launches.Count == priorLaunches + 5 && events.SequenceEqual(remote), "Repeated volley has exactly five distinct replicated launches " + cycle);
            Require(nodes <= stableNodes.Value + 12, $"Repeated cleanup has bounded scene nodes: {nodes}, baseline {stableNodes.Value}");
        }
        Require(host.World.State.Tick - soakStart > 60 * 60, "Repeated pickup/use/cleanup exercised more than one minute of authority time");

        await Death();
        _arenas[0].Driver.CollideMissile = native;

        async Task Death()
        {
            // End a real committed volley through native rival damage, then exercise
            // ordinary timed respawn. This is entity cleanup, never a player cancel.
            _heldShooter = new(0, Ground, -20);
            _heldTargets = (new(0, Ground, 25), new(80, Ground, 0));
            Position(_heldShooter.Value, _heldTargets.Value.First, _heldTargets.Value.Second);
            await Equip();
            Require(host.TryConfigure(0, new Dictionary<string, double> { ["items.maximum_damage"] = 10000, ["respawn.delay_ticks"] = 120 }, out _), "Lethal rival missile and timed respawn configured");
            host.Items.RemovePlayer(1);
            Require(host.Items.Grant(host.World, 1, HeldItem.Missile), "Rival receives a real Missile");
            await Until(() => host.Items.Slots.Single(s => s.Vehicle == 1) is { MissileReadyTick: > 0 } missile &&
                host.World.State.Tick >= missile.MissileReadyTick && _arenas[0].Driver.MissileReady?.Invoke(missile) != false,
                "Rival Missile finishes deployment");
            ulong life = host.World.GetVehicle(Shooter).LifeId;
            int priorLaunches = launches.Count;
            Require(driver.RequestItemUse(), "Final Salvo commits before lethal damage");
            await Until(() => launches.Count == priorLaunches + 1, "First round leaves before death");
            var rival = host.Items.Slots.Single(s => s.Vehicle == 1);
            var rivalPose = host.World.GetVehicle(1).ObservedPhysics;
            var origin = rivalPose.Position + N.Vector3.Transform(WeaponAim.Pivot, rivalPose.Orientation);
            var direction = N.Vector3.Normalize(host.World.GetVehicle(Shooter).ObservedPhysics.Position + N.Vector3.UnitY * .3f - origin);
            Require(host.AimItem(0, host.SessionId, rival.Life, rival.Token, rival.SelectionRevision, 1, direction), "Rival aims through the ordinary authoritative contract");
            await Frames(2);
            Require(_arenas[0].Driver.RequestItemUse(), "Rival fires its lethal Missile");
            await Until(() => !host.World.GetVehicle(Shooter).CanInteract, "Native Missile collision destroys the firing Salvo vehicle");
            Require(host.World.GetVehicle(Shooter).Damage.LastDamage?.Attribution.Source == "missile" &&
                !host.Items.Missiles.Any(m => m.Arc is not null && m.Owner == Shooter) &&
                !host.Items.Slots.Any(s => s.Vehicle == Shooter && s.ReservedSalvoShots > 0), "Death clears committed rounds and flight with ordinary missile attribution");
            // Stop the fixture's pose hold so it cannot replace the dead movement
            // state while the production respawn timer advances.
            _heldShooter = null;
            await Until(() => host.World.GetVehicle(Shooter).LifeId > life && host.World.GetVehicle(Shooter).CanInteract, "Ordinary timed respawn creates a fresh life");
            await Frames(180);
            Require(launches.Count == priorLaunches + 1 && _arenas.All(a => a.Bodies[Shooter].Rack.Catapult is null), "New life cannot resume the destroyed volley or retain its mechanism");
        }
    }

    private static int CountNodes(Node root) => 1 + root.GetChildren().Sum(CountNodes);
}
