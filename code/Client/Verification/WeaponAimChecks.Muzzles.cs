using Godot;
using Trackstorm.Client.Items;
using Trackstorm.Core.Items;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

public sealed partial class WeaponAimChecks
{
    private readonly int[,] _muzzleSamples = new int[3, 2];
    private readonly float[] _muzzleErrors = new float[3];
    private int _muzzleFailures;
    private bool _observingMuzzles;

    private void BeginMuzzleObservation()
    {
        RenderingServer.FramePreDraw += ObserveRenderedMuzzles;
        _observingMuzzles = true;
    }

    private void StopMuzzleObservation()
    {
        if (!_observingMuzzles) { return; }
        RenderingServer.FramePreDraw -= ObserveRenderedMuzzles;
        _observingMuzzles = false;
    }

    // Observe after all scene processing, independently of the pool's callback/order.
    // Reconstruct the rendered quad's tail and compare with the final barrel mesh transform.
    private void ObserveRenderedMuzzles()
    {
        for (int peer = 0; peer < _arenas.Count; peer++)
        {
            var arena = _arenas[peer];
            var fire = arena.GetChildren().OfType<ItemPresentation>().Single().MachineGunFire;
            for (int i = 0; i < fire.Multimesh.VisibleInstanceCount; i++)
            {
                if (fire.NewlyPresentedRound(i) is not { } shot) { continue; }
                int side = shot.MachineGunBarrel;
                _muzzleSamples[peer, side]++;
                var fang = arena.Bodies[shot.Owner].Rack.GetParent<Node3D>().GetNode<Node3D>("WeaponRack")
                    .FindChildren("*", "Node3D", true, false).OfType<TwinFangVisual>().SingleOrDefault();
                if (fang is null) { _muzzleFailures++; continue; }
                var quad = fire.Multimesh.GetInstanceTransform(i);
                float error = (quad.Origin - quad.Basis.Y * .5f).DistanceTo(fang.Muzzle(side));
                _muzzleErrors[peer] = Math.Max(_muzzleErrors[peer], error);
                var data = fire.Multimesh.GetInstanceCustomData(i);
                int expectedColor = shot.MachineGunRound % 6 >= 4 ? side + 1 : 0;
                if (Math.Abs(data.G * 2 - expectedColor) > .001f) { _muzzleFailures++; }
            }
        }
    }

    private void FinishMuzzleObservation()
    {
        StopMuzzleObservation();
        Require(_muzzleFailures == 0, "Rendered native rounds retain stable left red/right green gold/gold/colored identities");
        for (int peer = 0; peer < 3; peer++)
        {
            Require(_muzzleSamples[peer, 0] > 40 && _muzzleSamples[peer, 1] > 40 && _muzzleErrors[peer] < .0001f,
                $"Pre-draw peer {peer}: left={_muzzleSamples[peer, 0]} right={_muzzleSamples[peer, 1]} births; max endpoint error={_muzzleErrors[peer]:F6}m");
        }
    }

    private async Task CheckMovingMachineGun()
    {
        var host = _arenas[0].Driver.Host!;
        host.Items.RemovePlayer(Shooter);
        Position(new(0, Ground, 35), new(70, Ground, -25), new(-70, Ground, -25));
        _heldTargets = (new(70, Ground, -25), new(-70, Ground, -25));
        Require(host.Items.Grant(host.World, Shooter, HeldItem.MachineGun), "Fresh item deploys again before rapid moving aim");
        Camera.ResetFollow(); await Frames(180);
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
        await Frames(30);
        // Stage the same upward firing bearing independently of the driving/aim
        // framing offset. These measured bursts exercise actual rendered muzzles.
        Send(new InputEventMouseMotion { ScreenRelative = new(0, -(.33f - Camera.Rotation.X) / .003f) }); await Frames(60);
        var start = host.World.GetVehicle(Shooter).ObservedPhysics.Position;
        int before = _muzzleSamples[2, 0] + _muzzleSamples[2, 1];
        float speed = 0, turn = 0, yawRange = 0, pitchRange = 0;
        _firingPeers.Add(Shooter);
        for (int tick = 0; tick < 240; tick++)
        {
            if (tick % 12 == 0)
            { Send(new InputEventMouseMotion { ScreenRelative = new(tick % 48 < 24 ? 110 : -110, tick % 48 < 24 ? -35 : 35) }); }
            await Frames(1, throttle: ushort.MaxValue, steering: 6000, pressUse: tick == 0);
            var state = host.World.GetVehicle(Shooter);
            speed = Math.Max(speed, state.Speed);
            var forward = N.Vector3.Transform(-N.Vector3.UnitZ, state.ObservedPhysics.Orientation);
            turn = Math.Max(turn, Math.Abs(MathF.Atan2(forward.X, -forward.Z)));
            if (host.Items.Aims.FirstOrDefault(a => a.Vehicle == Shooter) is { } aim)
            { yawRange = Math.Max(yawRange, Math.Abs(aim.Yaw)); pitchRange = Math.Max(pitchRange, aim.Pitch); }
        }
        await Capture("mg-moving-rapid-aim");
        _firingPeers.Clear(); await Frames(50);
        float distance = N.Vector3.Distance(start, host.World.GetVehicle(Shooter).ObservedPhysics.Position);
        int births = _muzzleSamples[2, 0] + _muzzleSamples[2, 1] - before;
        Require(speed > 5 && distance > 5 && turn > .1f && yawRange > .3f && pitchRange > .3f && births > 100,
            $"Moving rapid yaw/pitch: speed={speed:F2}m/s travel={distance:F2}m turn={turn:F3}rad yaw={yawRange:F3}rad pitch={pitchRange:F3}rad observer births={births}");
    }
}
