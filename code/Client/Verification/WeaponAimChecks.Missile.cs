using Godot;
using Trackstorm.Client.Items;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

public sealed partial class WeaponAimChecks
{
    private async Task CheckMissileAim()
    {
        var host = _arenas[0].Driver.Host!;
        var driver = _arenas[1].Driver;
        var native = _arenas[0].Driver.CollideMissile!;
        var launches = new Dictionary<ulong, MissileState>();
        var outcomes = new List<ItemEvent>();
        var remote = new List<ItemEvent>();
        _arenas[0].Driver.CollideMissile = (missile, end) =>
        {
            launches.TryAdd(missile.Id, missile);
            return native(missile, end);
        };
        _arenas[0].Driver.ItemsReceived += state => outcomes.AddRange(state.Events.Where(e => e.Item == HeldItem.Missile));
        _arenas[2].Driver.ItemsReceived += state => remote.AddRange(state.Events.Where(e => e.Item == HeldItem.Missile));
        async Task Equip()
        {
            foreach (ulong id in host.World.State.Vehicles.Select(v => v.VehicleId)) { host.Items.RemovePlayer(id); }
            await Frames(110);
            Require(host.Items.Grant(host.World, Shooter, HeldItem.Missile), "Fresh one-shot Missile grant");
            await Frames(5);
            Require(!driver.RequestItemUse(), "Deployment rejects local tap without queuing");
            var slot = host.Items.Slots.Single(s => s.Vehicle == Shooter);
            Require(!host.Items.RequestUse(host.World, Shooter, slot.Life, slot.Token), "Authority rejects deploying tap");
            await Until(() => driver.LocalItem?.Active.Item == HeldItem.Missile && driver.MissileReady?.Invoke(driver.LocalItem) == true,
                "Production Missile completes deployment");
            await Frames(35);
            Require(!launches.ContainsKey(slot.Token), "Deployment did not replay the rejected tap");
        }
        async Task Fire(string name, ulong? target = null)
        {
            var slot = driver.LocalItem!;
            var accepted = host.Items.AcceptedAim(Shooter, slot.Life, slot.Active.Token);
            float targetHp = target.HasValue ? host.World.GetVehicle(target.Value).Damage.CurrentHP : 0;
            Require(accepted is not null, name + ": shared accepted aim is ready");
            CheckMissileMount(name);
            await Capture("missile-" + name + "-ready");
            await CaptureMissileMount(name);
            Require(driver.RequestItemUse(), name + ": ordinary remote use sent");
            await Until(() => launches.ContainsKey(slot.Active.Token), name + ": authoritative swept projectile launched");
            var launch = launches[slot.Active.Token];
            Require(N.Vector3.Dot(N.Vector3.Normalize(launch.Velocity), accepted!.Direction) > .995f,
                name + ": initial velocity follows accepted aim");
            Require(N.Vector3.Distance(launch.Position, accepted.Origin) < .2f, name + ": production pivot is launch origin");
            var orientation = host.World.GetVehicle(Shooter).ObservedPhysics.Orientation;
            Require(!WeaponAim.IntersectsBody(WeaponAim.Pivot, N.Vector3.Transform(launch.Velocity, N.Quaternion.Conjugate(orientation))),
                name + ": launch ray clears self vehicle");
            await Until(() => driver.LocalItem?.Active.Item == HeldItem.None, name + ": reliable depletion reaches shooter");
            Require(!driver.RequestItemUse(), name + ": depleted one-shot capability cannot fire again");
            await Capture("missile-" + name + "-flight");
            if (target.HasValue)
            {
                await Until(() => outcomes.Any(e => e.Token == slot.Active.Token && e.Impact), name + ": native impact");
                Require(host.World.GetVehicle(target.Value).Damage.CurrentHP < targetHp, name + ": aimed native hit damages selected car");
            }
            else
            {
                // Later camera/target movement has no vehicle tracking authority.
                Send(new InputEventMouseMotion { ScreenRelative = new(80, -20) });
                await Frames(15);
                var flight = host.Items.Missiles.FirstOrDefault(m => m.Id == launch.Id);
                if (flight is not null)
                {
                    var firstHeading = new N.Vector2(launch.Velocity.X, launch.Velocity.Z);
                    var currentHeading = new N.Vector2(flight.Velocity.X, flight.Velocity.Z);
                    Require(firstHeading.Length() < .01f || N.Vector2.Dot(N.Vector2.Normalize(firstHeading), N.Vector2.Normalize(currentHeading)) > .99999f,
                        name + ": post-launch camera movement cannot steer horizontal flight");
                }
            }
            await Until(() => _arenas.All(a => a.Bodies[Shooter].Rack.Progress <= .001f), name + ": empty mount/rack/trunk fully stows");
        }
        foreach (var shot in new (string Name, Vector3 Offset, bool Aiming)[]
        {
            ("normal-forward", new(0, 0, -100), false),
            ("free-forward", new(0, 2, -100), true),
            ("free-side", new(100, 8, 0), true),
            ("free-rear", new(0, 8, 100), true),
            ("free-elevated", new(0, 100, -80), true),
            ("free-downward", new(0, -18, 80), true),
            ("interception", new(-60, 25, -40), true),
        })
        {
            _heldShooter = new(0, Ground, 35);
            _heldTargets = (new(70, Ground, -70), new(-70, Ground, -70));
            Position(_heldShooter.Value, _heldTargets.Value.First, _heldTargets.Value.Second);
            Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
            Camera.ResetFollow(); await Frames(10); await Equip();
            if (shot.Aiming) { await AimAt(Camera.GlobalPosition + shot.Offset); }
            await Frames(70);
            Require(_arenas[1].AssistedCar == 0 && !_arenas[1].TargetIndicator.ChevronVisible, shot.Name + ": unlocked aim has no lock marker");
            if (!shot.Aiming) { Require(N.Vector3.Dot(driver.DesiredAim!.Value, DrivingWeaponAim.Direction(-N.Vector3.UnitZ, N.Vector3.UnitY)) > .99999f, "RMB-up driving intent is road-forward"); }
            await Fire(shot.Name);
        }
        // The same two rival identities exercise acquisition, retained marker and switching.
        _heldShooter = new(0, Ground, 35);
        _heldTargets = (new(-7, Ground, 0), new(7, Ground, 0));
        Position(_heldShooter.Value, _heldTargets.Value.First, _heldTargets.Value.Second);
        Camera.ResetFollow(); await Equip();
        foreach (ulong id in new[] { 1ul, _arenas[2].Driver.LocalVehicleId, 1ul })
        {
            await AimAtCurrent(() => _arenas[1].Bodies[id].VisualTransform * new Vector3(0, .3f, 0));
            await Frames(60);
            Require(_arenas[1].AssistedCar == id && _arenas[1].TargetIndicator.Target == id &&
                _arenas[1].TargetIndicator.ChevronVisible && _arenas[1].TargetIndicator.RingVisible,
                $"Shared Missile lock selects/switches car {id}, with ground ring and overhead chevron");
            await Capture("missile-lock-" + id);
        }
        await Fire("locked-first", 1);
        await Equip();
        ulong second = _arenas[2].Driver.LocalVehicleId;
        await AimAtCurrent(() => _arenas[1].Bodies[second].VisualTransform * new Vector3(0, .3f, 0));
        await Frames(70); await Fire("locked-second", second);
        await Equip();
        await AimAtCurrent(() => _arenas[1].Bodies[1].VisualTransform * new Vector3(0, .3f, 0));
        await Frames(40);
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false }); await Frames(75);
        Require(_arenas[1].AssistedCar == 0 && N.Vector3.Dot(driver.DesiredAim!.Value, DrivingWeaponAim.Direction(-N.Vector3.UnitZ, N.Vector3.UnitY)) > .99999f,
            "Releasing camera aim clears lock and restores normal forward intent");
        await Fire("released-forward");
        await Equip(); Camera.ResetFollow(); await Frames(10);
        Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = .7f }); await Frames(30);
        Require(_input.Adapter.CameraAimActive && Math.Abs(host.Items.Aims.Single(a => a.Vehicle == Shooter).Yaw) > .03f,
            "Existing controller camera input supplies Missile aim without RMB");
        Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = 0 });
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
        Camera.ResetFollow(); await Frames(5);
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
        Send(new InputEventMouseMotion { ScreenRelative = new(0, 500) }); await Frames(90);
        Require(!host.Items.Aims.Single(a => a.Vehicle == Shooter).Ready, "Self-obstructed downward aim is not ready");
        ulong blocked = driver.LocalItem!.Active.Token;
        driver.RequestItemUse(); await Frames(40);
        Require(!launches.ContainsKey(blocked) && driver.LocalItem.Active.Token == blocked, "Blocked fire preserves round and does not launch through Car");
        // Observe articulation during ordinary native movement and a fixture-initialized ballistic jump.
        _heldShooter = null; _heldTargets = null;
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false }); Camera.ResetFollow(); await Frames(70);
        for (int frame = 0; frame < 150; frame++) { await Frames(1, throttle: 26000, steering: frame < 75 ? (short)6000 : (short)-6000); }
        await Capture("missile-driving");
        var world = host.World.State;
        host.World.Restore(new(world.Tick, world.LastInput, world.Vehicles.Select(state =>
        {
            if (state.VehicleId != Shooter) { return state; }
            var pose = new VehiclePhysicsState(state.ObservedPhysics.Position + N.Vector3.UnitY * 7,
                state.ObservedPhysics.Orientation, new(4, 7, 0), N.Vector3.Zero);
            _arenas[0].Bodies[Shooter].Apply(pose);
            return new VehicleSnapshot(state.VehicleId, state.LifeId, new(world.Tick, pose, false, false, 0, 0), state.Damage, pose);
        }), world.Match));
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
        Send(new InputEventMouseMotion { ScreenRelative = new(450, -80) });
        await Frames(35); await Capture("missile-airborne-articulation");
        Require(!host.World.GetVehicle(Shooter).Movement.Grounded && driver.MissileReady!.Invoke(driver.LocalItem!), "Ready launcher remains deployed during native flight");
        Require(driver.RequestItemUse(), "Airborne aimed launch accepted");
        await Until(() => launches.ContainsKey(blocked), "Airborne projectile launched");
        Require(host.Items.Grant(host.World, Shooter, HeldItem.MachineGun), "Next weapon pickup retained during fired Missile return");
        ulong replacement = host.Items.Slots.Single(s => s.Vehicle == Shooter).Active.Token;
        await Frames(10);
        Require(_arenas[1].Bodies[Shooter].Rack.PresentedItem == HeldItem.Missile, "Pickup cannot interrupt returning empty Missile launcher");
        await Until(() => _arenas[1].Bodies[Shooter].Rack.Progress <= .001f, "Complete stow precedes new weapon deployment");
        await Until(() => _arenas[1].Bodies[Shooter].Rack.PresentedItem == HeldItem.MachineGun, "Replacement deploys after stow");
        Require(host.Items.Slots.Single(s => s.Vehicle == Shooter).Active.Token == replacement, "Replacement ownership survives return");
        await Until(() => host.World.GetVehicle(Shooter).Movement.Grounded, "Native landing completes");
        await Frames(70); await Capture("missile-landed-handoff");
        Require(outcomes.SequenceEqual(remote), $"Observer receives identical ordered launch/impact outcomes ({outcomes.Count}) over real UDP");
        _arenas[0].Driver.CollideMissile = native;
    }

    private void CheckMissileMount(string phase)
    {
        foreach (var arena in _arenas)
        {
            var rack = arena.Bodies[Shooter].Rack.GetParent<Node3D>().GetNode<Node3D>("WeaponRack");
            var launcher = rack.GetNode<MissileLauncher>("MissileLauncher");
            var yaw = (Node3D)launcher.FindChild("TurretYaw", true, false);
            var pitch = (Node3D)launcher.FindChild("CradlePitch", true, false);
            var aim = arena.Driver.AcceptedAims.Single(a => a.Vehicle == Shooter);
            Require(rack.Rotation.Length() < .001f && launcher.Scale.IsEqualApprox(Vector3.One), phase + ": production rack remains fixed and full scale");
            Require(Math.Abs(Mathf.AngleDifference(yaw.Rotation.Y, aim.Yaw)) < .035f && Math.Abs(Mathf.AngleDifference(pitch.Rotation.X, aim.Pitch)) < .035f,
                phase + ": production turret and upper mount follow accepted yaw/pitch on peer " + arena.Driver.LocalVehicleId);
            Require(pitch.GlobalPosition.DistanceTo(arena.Bodies[Shooter].VisualTransform * VehicleBody.ToGodot(WeaponAim.Pivot)) < .02f,
                phase + ": authored Missile socket equals shared launch pivot");
        }
    }

    private async Task CaptureMissileMount(string phase)
    {
        if (DisplayServer.GetName() == "headless") { return; }
        var arena = _arenas[2];
        var body = arena.Bodies[Shooter];
        var view = _views[2];
        var camera = new Camera3D { Fov = 50, Far = 500 };
        arena.AddChild(camera);
        camera.GlobalPosition = body.VisualTransform * new Vector3(3.4f, 3.3f, 4.7f);
        camera.LookAt(body.VisualTransform * new Vector3(0, 1.5f, 1.845f));
        camera.Current = true; view.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = view.GetTexture().GetImage();
        image.SavePng(System.IO.Path.Combine(_output, "missile-mount-" + phase + ".png"));
        camera.QueueFree(); view.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
    }
}
