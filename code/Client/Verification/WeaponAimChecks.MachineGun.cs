using Godot;
using Trackstorm.Core.Items;
using Trackstorm.Core.Input;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

public sealed partial class WeaponAimChecks
{
    private readonly HashSet<ulong> _firingPeers = [];

    private async Task CheckMachineGunFire()
    {
        var host = _arenas[0].Driver.Host!;
        Position(new(0, Ground, 35), new(0, Ground, 0), new(70, Ground, 0));
        host.Items.Grant(host.World, Shooter, HeldItem.MachineGun);
        host.Items.Grant(host.World, Shooter, HeldItem.Wrench);
        await Frames(180);
        var actual = new List<ItemEvent>();
        var remote = new List<ItemEvent>();
        void Record(ItemPublication state, List<ItemEvent> destination) => destination.AddRange(state.Events.Where(e => e.Item == HeldItem.MachineGun));
        void HostEvents(ItemPublication state) => Record(state, actual);
        void RemoteEvents(ItemPublication state) => Record(state, remote);
        _arenas[0].Driver.ItemsReceived += HostEvents;
        _arenas[2].Driver.ItemsReceived += RemoteEvents;
        var raycast = _arenas[0].Driver.RaycastWeapon!;
        int rays = 0, hits = 0;
        _arenas[0].Driver.RaycastWeapon = (owner, start, end) =>
        {
            var aim = host.Items.Aims.Single(a => a.Vehicle == owner);
            RequireShot(aim.Ready && N.Vector3.Distance(start, aim.Origin) < .001f, "Shot uses current ready accepted origin");
            var direction = N.Vector3.Normalize(end - start);
            RequireShot(N.Vector3.Dot(direction, aim.Direction) >= MathF.Cos(host.Items.Configuration.MachineGunSpread * MathF.PI / 180) - .0001f,
                "Shot stays in configured cone around accepted direction");
            rays++;
            var hit = raycast(owner, start, end);
            if (hit?.Vehicle == 1) { hits++; }
            return hit;
        };
        host.TryConfigure(0, new Dictionary<string, double> { ["items.machine_gun_spread"] = 0 }, out _);
        foreach (var scenario in new (string Name, N.Vector3 Target)[]
        {
            ("forward", new(0, Ground, 0)), ("side", new(20, Ground, 35)),
            ("rear", new(0, Ground, 45)), ("airborne", new(0, Ground + 12, 0)),
            ("close-side", new(-4.5f, Ground, 35)),
        })
        {
            _heldTargets = (scenario.Target, new(70, Ground, 0));
            await Frames(40);
            Camera.ResetFollow();
            await AimAtCurrent(() => _arenas[1].Bodies[1].VisualPosition + Vector3.Up * .6f);
            await Frames(70);
            LogTargetGeometry(scenario.Name, 1);
            Require(host.Items.Aims.Single(a => a.Vehicle == Shooter).Ready, $"Machine Gun {scenario.Name} accepted ready");
            GD.Print($"MG_GEOMETRY {scenario.Name}: aim={host.Items.Aims.Single(a => a.Vehicle == Shooter)} target={host.World.GetVehicle(1).ObservedPhysics.Position} cameraDesired={_arenas[1].Driver.DesiredAim}");
            int before = rays, beforeHits = hits;
            float hp = host.World.GetVehicle(1).Damage.CurrentHP;
            Require(_arenas[1].Driver.RequestItemUse(), "Camera shooter submits ordinary held-use request");
            _firingPeers.Add(Shooter);
            await Frames(20);
            await Capture("mg-" + scenario.Name);
            _firingPeers.Clear(); await Frames(40);
            Require(rays > before && hits > beforeHits && host.World.GetVehicle(1).Damage.CurrentHP < hp,
                $"Machine Gun {scenario.Name}: native hit/damage, {rays - before} rounds, {hits - beforeHits} target hits");
            RequireObserverMount("firing-" + scenario.Name);
            var body = _arenas[2].Bodies[Shooter];
            var rack = body.Rack.GetParent<Node3D>().GetNode<Node3D>("WeaponRack");
            var mount = rack.GetNode<Node3D>("WeaponYaw");
            Require(Math.Abs(rack.Rotation.Y) < .0001f && Math.Abs(rack.Rotation.Z) < .0001f &&
                mount.GlobalPosition.DistanceTo(body.VisualTransform * Trackstorm.Client.Vehicles.VehicleBody.ToGodot(WeaponAim.Pivot)) < .04f &&
                mount.Scale.IsEqualApprox(Vector3.One), $"{scenario.Name}: fixed rack, full payload scale and shared pivot origin align on observer");
        }
        // Down through the shooter's chassis: intent and HUD remain live, authority refuses fire.
        Camera.ResetFollow(); await Frames(5);
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
        Send(new InputEventMouseMotion { ScreenRelative = new(0, 500) });
        await Frames(90);
        Require(!host.Items.Aims.Single(a => a.Vehicle == Shooter).Ready, "Self-blocked camera direction is not firing-ready");
        int blockedRounds = host.Items.Slots.Single(s => s.Vehicle == Shooter).Ammo!.Remaining;
        _arenas[1].Driver.RequestItemUse(); _firingPeers.Add(Shooter); await Frames(40);
        Require(host.Items.Slots.Single(s => s.Vehicle == Shooter).Ammo!.Remaining == blockedRounds, "Self-blocked held fire spends no rounds");
        _firingPeers.Clear(); await Frames(10);
        _heldTargets = (new(0, Ground + 12, 0), new(70, Ground, 0));
        Camera.ResetFollow(); await Frames(30);
        await AimAtCurrent(() => _arenas[1].Bodies[1].VisualPosition + Vector3.Up);
        await Frames(60);
        host.TryConfigure(0, new Dictionary<string, double> { ["items.machine_gun_spread"] = 6 }, out _);
        // Production-map target staging may have claimed a pickup along its path.
        // This phase explicitly owns both shooter loadouts, not that incidental item.
        host.Items.RemovePlayer(1);
        Require(host.Items.Grant(host.World, 1, HeldItem.MachineGun), "Second shooter receives a fresh selected Machine Gun");
        _firingPeers.Add(1); // Fresh host intent while it deploys; no use request yet.
        await Frames(180);
        Require(_arenas[0].Driver.RequestItemUse() && _arenas[1].Driver.RequestItemUse(), "Both shooters submit sustained activation");
        _firingPeers.Add(Shooter);
        int beforeSustain = rays;
        await Frames(120); await Capture("mg-multiple-sustained");
        _firingPeers.Clear(); await Frames(50);
        Require(rays - beforeSustain >= 300 && actual.Select(e => e.Owner).Distinct().Count() == 2,
            $"Two simultaneous shooters sustain default spread and cadence through real UDP ({rays - beforeSustain} rounds)");
        Require(actual.SequenceEqual(remote), $"Observer receives identical ordered firing endpoints/tracers ({actual.Count} rounds)");
        int released = rays; await Frames(40);
        Require(rays == released, "Release stops actual firing on both peers");

        // Keep held use active but stop submitting camera aim. Test stale aim independently of release.
        _arenas[1].Driver.RequestItemUse(); _firingPeers.Add(Shooter); await Frames(15);
        _arenas[1].CameraInput = null; await Frames(50);
        int expired = rays; await Frames(30);
        Require(rays == expired && !host.Items.Aims.Any(a => a.Vehicle == Shooter), "Aim expiry stops held fire without stale-forward fallback");
        _arenas[1].CameraInput = _input.Adapter; await Frames(60);
        Require(rays > expired, "Fresh camera intent resumes held fire after aim expiry");
        _firingPeers.Clear(); await Frames(40);
        _input.Adapter.GameplaySuppressed = true; await Frames(30);
        int suppressed = rays; await Frames(30);
        Require(rays == suppressed && !_arenas[1].AimOverlay.Marker.HasValue, "Suppression clears camera aiming and firing presentation");
        _input.Adapter.GameplaySuppressed = false;
        _arenas[1].Driver.RequestItemSwitch(); await Frames(45);
        Require(_arenas[1].Driver.LocalItem?.Active.Item == HeldItem.Wrench, "Switch retires Machine Gun capability");
        _arenas[1].Driver.RequestItemSwitch(); await Frames(180);
        Require(rays == suppressed, "Switch back does not replay earlier sustained activation");

        _arenas[0].Driver.RaycastWeapon = raycast;
        _arenas[0].Driver.ItemsReceived -= HostEvents;
        _arenas[2].Driver.ItemsReceived -= RemoteEvents;
        foreach (ulong id in new ulong[] { 1, Shooter }) { host.Items.RemovePlayer(id); }
        _heldTargets = null;
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
        Camera.ResetFollow(); await Frames(40);
        Require(true, $"Machine Gun camera firing verified: {rays} native rays, {hits} target hits; fixed rack uses existing yaw/pitch payload");
    }

    private static void RequireShot(bool condition, string message)
    { if (!condition) { throw new InvalidOperationException(message); } }
}
