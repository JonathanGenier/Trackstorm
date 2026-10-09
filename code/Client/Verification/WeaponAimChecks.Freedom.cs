using Godot;
using Trackstorm.Core.Items;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

public sealed partial class WeaponAimChecks
{
    private async Task CheckAimFreedom()
    {
        await CheckHeldAimLifecycle();
        var host = _arenas[0].Driver.Host!;
        float cone = host.Items.Configuration.Aim.AssistDegrees;
        void Assist(bool enabled) => host.TryConfigure(0, new Dictionary<string, double> { ["items.aim_assist_degrees"] = enabled ? cone : 0 }, out _);
        Vector3 Center() => _arenas[1].Bodies[1].VisualTransform * new Vector3(0, .3f, 0);
        float Error() => Camera.ProjectRayNormal(ViewCenter).AngleTo(Center() - Camera.GlobalPosition);
        host.Items.RemovePlayer(Shooter);
        Position(new(0, Ground, 35), new(0, Ground, 0), new(70, Ground, 0));
        host.Items.Grant(host.World, Shooter, HeldItem.MachineGun);
        _heldTargets = (new(0, Ground, 0), new(70, Ground, 0));
        Assist(false); Camera.ResetFollow(); await Frames(150);
        await AimAtCurrent(() => Center() + Vector3.Right * 3.2f); await Frames(30);
        Require(_arenas[1].AssistedCar == 0 && Error() > .05f, "Near-car acquisition starts from a genuine cursor miss with assistance disabled");
        Assist(true); await Frames(2);
        Send(new InputEventMouseMotion { ScreenRelative = new(-1, 0) }); await Frames(120);
        Require(_arenas[1].AssistedCar == 1 && Error() < .008f, $"Forgiving acquisition survives small approach input and converges: error={Error():F5} rad");
        for (int i = 0; i < 4; i++)
        {
            Send(new InputEventMouseMotion { ScreenRelative = new(1, 0) }); await Frames(4);
        }
        await Frames(20);
        float adjusted = Error(); await Frames(60);
        Require(_arenas[1].AssistedCar == 1 && adjusted > .007f && Math.Abs(Error() - adjusted) < .003f,
            $"Fine placement remains off-centre while RMB stays held: {adjusted:F5}->{Error():F5} rad");
        int placementShots = 0;
        N.Vector3 sum = N.Vector3.Zero;
        var native = _arenas[0].Driver.RaycastWeapon!;
        _arenas[0].Driver.RaycastWeapon = (owner, start, end) =>
        {
            if (owner == Shooter) { placementShots++; sum += N.Vector3.Normalize(end - start); }
            return native(owner, start, end);
        };
        _firingPeers.Add(Shooter); await Frames(30, pressUse: true);
        _firingPeers.Clear(); await Frames(30); _arenas[0].Driver.RaycastWeapon = native;
        var aim = host.Items.Aims.Single(a => a.Vehicle == Shooter);
        var centreDirection = N.Vector3.Normalize(Vehicles.VehicleBody.ToCore(Center()) - aim.Origin);
        float shotOffset = MathF.Acos(Math.Clamp(N.Vector3.Dot(N.Vector3.Normalize(sum), centreDirection), -1, 1));
        Require(placementShots > 20 && shotOffset < .003f && N.Vector3.Dot(centreDirection, aim.Direction) > .99999f,
            $"Retained target keeps actual authoritative shots centred despite camera offset: {placementShots} rays, {shotOffset:F5} rad off centre");
        await Capture("freedom-fine-placement");

        Send(new InputEventMouseMotion { ScreenRelative = new(70, 0) }); await Frames(60);
        Require(_arenas[1].AssistedCar == 0, "Outward departure releases target before returning to cursor-directed shots");
        placementShots = 0; sum = N.Vector3.Zero;
        _arenas[0].Driver.RaycastWeapon = (owner, start, end) =>
        {
            if (owner == Shooter) { placementShots++; sum += N.Vector3.Normalize(end - start); }
            return native(owner, start, end);
        };
        _firingPeers.Add(Shooter); await Frames(30, pressUse: true);
        _firingPeers.Clear(); await Frames(30); _arenas[0].Driver.RaycastWeapon = native;
        aim = host.Items.Aims.Single(a => a.Vehicle == Shooter);
        centreDirection = N.Vector3.Normalize(Vehicles.VehicleBody.ToCore(Center()) - aim.Origin);
        Require(placementShots > 20 && _arenas[1].AssistedCar == 0 &&
            N.Vector3.Dot(aim.Direction, _arenas[1].Driver.DesiredAim!.Value) > .9999f &&
            N.Vector3.Dot(N.Vector3.Normalize(sum), aim.Direction) > .9999f && N.Vector3.Dot(centreDirection, aim.Direction) < .995f,
            $"After target loss actual shots follow accepted free aim away from car centre: {placementShots} rays");

        float unassisted = 0;
        foreach (bool enabled in new[] { false, true })
        {
            Assist(enabled);
            Position(new(0, Ground, 35), new(0, Ground, -25), new(70, Ground, -25));
            _heldTargets = (new(0, Ground, -25), new(70, Ground, -25));
            Camera.ResetFollow(); await Frames(120); await AimAtCurrent(Center); await Frames(45);
            // Neutral look while actual native steering/throttle run. Rivals use a stated fixture trajectory.
            Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
            N.Vector3 startPosition = host.World.GetVehicle(Shooter).ObservedPhysics.Position;
            float errorSum = 0, peakSpeed = 0, turn = 0;
            int retained = 0, rounds = 0, hits = 0;
            _arenas[0].Driver.RaycastWeapon = (owner, start, end) =>
            {
                var hit = native(owner, start, end);
                if (owner == Shooter) { rounds++; if (hit?.Vehicle == 1) { hits++; } }
                return hit;
            };
            _firingPeers.Add(Shooter);
            for (int i = 0; i < 180; i++)
            {
                _heldTargets = (new(i * .025f, Ground, -25), new(70, Ground, -25));
                await Frames(1, throttle: ushort.MaxValue, steering: 6000, pressUse: i == 0);
                errorSum += Error();
                if (_arenas[1].AssistedCar == 1) { retained++; }
                var state = host.World.GetVehicle(Shooter);
                peakSpeed = Math.Max(peakSpeed, state.Speed);
                var forward = N.Vector3.Transform(-N.Vector3.UnitZ, state.ObservedPhysics.Orientation);
                turn = Math.Max(turn, Math.Abs(MathF.Atan2(forward.X, -forward.Z)));
            }
            // PNG encoding can stall the script long enough for the existing network
            // input-silence gate to release sustained use. Capture after the measured drive.
            if (enabled) { await Capture("freedom-driving"); }
            _firingPeers.Clear(); await Frames(30); _arenas[0].Driver.RaycastWeapon = native;
            float mean = errorSum / 180;
            float distance = N.Vector3.Distance(startPosition, host.World.GetVehicle(Shooter).ObservedPhysics.Position);
            Require(peakSpeed > 5 && distance > 5 && turn > .08f && rounds > 100,
                $"Driving/fire exercised assist={enabled}: speed={peakSpeed:F2}m/s distance={distance:F2}m turn={turn:F3}rad rays={rounds} hits={hits} retained={retained}/180 meanError={mean:F5}rad");
            if (!enabled) { unassisted = mean; }
            else
            {
                Require(retained >= 170 && mean < .025f && mean < unassisted * .3f && hits > 100,
                    "Driving compensation retains engagement and native hits while materially reducing tracking error");
                float beforeBraking = host.World.GetVehicle(Shooter).Speed;
                await Frames(30, brake: ushort.MaxValue);
                Require(_arenas[1].AssistedCar == 1,
                    $"Braking is not aim-away input: speed={beforeBraking:F2}->{host.World.GetVehicle(Shooter).Speed:F2}m/s, same retained car");
            }
        }
        Send(new InputEventMouseMotion { ScreenRelative = new(70, 0) });
        await Frames(8); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Require(_arenas[1].AssistedCar == 0, "Deliberate aim-away releases even after sustained driving engagement");
        Position(new(0, Ground, 35), new(-4, Ground, 0), new(4, Ground, 0));
        _heldTargets = (new(-4, Ground, 0), new(4, Ground, 0));
        Camera.ResetFollow(); await Frames(90); await AimAtCurrent(Center);
        Require(_arenas[1].AssistedCar == 1, "First rival can be deliberately acquired before switching");
        ulong other = _arenas[1].Bodies.Keys.Single(id => id != Shooter && id != 1);
        await AimAtCurrent(() => _arenas[1].Bodies[other].VisualTransform * new Vector3(0, .3f, 0)); await Frames(30);
        Require(_arenas[1].AssistedCar == other, "Deliberate sweep switches to the chosen second car");
        for (int i = 0; i < 50 && _arenas[1].AssistedCar != 0; i++)
        {
            Send(new InputEventMouseMotion { ScreenRelative = new(1, 0) }); await Frames(2);
        }
        Require(_arenas[1].AssistedCar == 0, "Slow deliberate adjustment beyond the frame also releases");
        await Frames(45);
        Require(_arenas[1].AssistedCar == 0, "Stopping beside the dismissed car does not pull free aim back onto it");
        Send(new InputEventMouseMotion { ScreenRelative = new(-1, 0) }); await Frames(60);
        Require(_arenas[1].AssistedCar == other, "Deliberately returning toward the dismissed car rearms acquisition");
        Send(new InputEventMouseMotion { ScreenRelative = new(70, 0) }); await Frames(8);
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
        await Frames(120);
        RequireCentered("Chase return after engagement preserves the centred logical cursor");
        var chaseForward = N.Vector3.Transform(-N.Vector3.UnitZ, _arenas[1].Driver.LocalState!.ObservedPhysics.Orientation);
        float chaseYaw = MathF.Atan2(-chaseForward.X, -chaseForward.Z);
        Require(Math.Abs(Mathf.AngleDifference(Camera.Rotation.Y, chaseYaw)) < .02f,
            "Released neutral camera returns behind the vehicle instead of retaining a target camera");
        _heldTargets = null; Camera.ResetFollow(); Assist(true); await Frames(30);
        await CheckHeldAimFlight();
    }
}
