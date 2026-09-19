using Godot;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Verification;

/// <summary>Native production-default lift-off, steering and sprung-body checks on isolated test geometry.</summary>
public sealed partial class OvalIntegrationChecks
{
    private async Task VerifyBumpsAndCoasting()
    {
        // An isolated long runway permits reaching the powered cap without an automated cornering controller.
        var runway = new StaticBody3D { Position = new Vector3(0, 19.5f, 400) };
        runway.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(3000, 1, 40) } });
        AddChild(runway);
        await Frames(2);
        Basis facing = new(Vector3.Up, -Mathf.Pi / 2);
        foreach (bool network in new[] { false, true })
        {
            string adapter = network ? "Network" : "Practice";
            var powered = await HandlingProbe(network, new Vector3(-1200, 20.9f, 400), facing, Vector3.Zero, 1800, tick => new InputFrame(tick, 0, ushort.MaxValue, 0, 0, 0, 0));
            float speed = powered[^1].CommandSpeed;
            Check(speed > 44.3f && speed <= 44.441f && powered.Skip(2).All(state => state.Grounded && Math.Abs(state.Physics.AngularVelocity.Y) < 0.02f), $"{adapter} production propulsion reaches {speed:F4} m/s with neutral steering and stable support.");
            var coast = await HandlingProbe(network, new Vector3(0, 20.9f, 400), facing, Vector3.Right * 44.44f, 601, tick => new InputFrame(tick, 0, 0, 0, 0, 0, 0));
            Check(coast[120].CommandSpeed < 17 && coast[^1].CommandSpeed < 0.4f && coast.Skip(2).All(state => state.Physics.LinearVelocity.X > 0), $"{adapter} lift-off: {coast[120].CommandSpeed:F3} m/s after two seconds, {coast[^1].CommandSpeed:F3} m/s after ten seconds, no reversal.");

            foreach (float entry in new[] { 8f, 28f })
            {
                var turn = await HandlingProbe(network, new Vector3(0, 20.9f, 400), facing, Vector3.Right * entry, 120, tick => new InputFrame(tick, tick < 31 ? (short)8000 : (short)0, ushort.MaxValue, 0, 0, 0, 0));
                float yaw = turn.Skip(2).Max(state => Math.Abs(state.Physics.AngularVelocity.Y));
                float finalYaw = Math.Abs(turn[^1].Physics.AngularVelocity.Y);
                Check(yaw is > 0.03f and < 0.8f && finalYaw < 0.1f && turn.Skip(2).All(state => state.Grounded), $"{adapter} ordinary {entry} m/s turn then neutral throttle: peak yaw {yaw:F3}, final yaw {finalYaw:F3} rad/s.");
            }
        }

        // Smooth 12 cm crest, four metres long; this is a test fixture, not a production map edit.
        var faces = new List<Vector3>();
        for (int section = 0; section < 32; section++)
        {
            float x0 = -2 + (section / 8f);
            float x1 = x0 + 0.125f;
            float h0 = 0.12f * MathF.Pow(MathF.Cos(x0 * MathF.PI / 4), 2);
            float h1 = 0.12f * MathF.Pow(MathF.Cos(x1 * MathF.PI / 4), 2);
            Vector3 a = new(x0, h0, -10);
            Vector3 b = new(x0, h0, 10);
            Vector3 c = new(x1, h1, 10);
            Vector3 d = new(x1, h1, -10);
            faces.AddRange(new[] { a, c, b, a, d, c });
        }

        var bump = new StaticBody3D { Position = new Vector3(0, 20, 400) };
        bump.AddChild(new CollisionShape3D { Shape = new ConcavePolygonShape3D { Data = faces.ToArray() } });
        AddChild(bump);
        await Frames(2);
        var crest = Hit(new Vector3(0, 20.12f, 400));
        Check(crest.Body == bump && Math.Abs(crest.Position.Y - 20.12f) < 0.001f && crest.Normal.Y > 0.99f, "Bump fixture exposes its upward-facing 12 cm crest to production wheel queries.");
        foreach (bool network in new[] { false, true })
        {
            foreach (float entry in new[] { 6f, 12f, 20f })
            {
                var states = await HandlingProbe(network, new Vector3(-8, 20.9f, 400), facing, Vector3.Right * entry, 240, tick => new InputFrame(tick, 0, ushort.MaxValue, 0, 0, 0, 0));
                float rise = states.Skip(2).Max(state => state.Physics.Position.Y - 20.9f);
                float compression = states.Skip(2).Max(state => Math.Max(state.Wheels.Compression.X, state.Wheels.Compression.Z));
                float settled = states.TakeLast(60).Max(state => Math.Abs(state.Physics.LinearVelocity.Y));
                int airborne = states.Skip(2).Count(state => !state.Grounded);
                string adapter = network ? "Network" : "Practice";
                Check(rise is > 0.01f and < 0.14f && compression > 0.11f && settled < 0.02f && airborne == 0, $"{adapter} 12 cm bump at {entry} m/s: chassis rise {rise:F3} m, compression {compression:F3} m, settling {settled:F4} m/s, unsupported {airborne}.");
            }
        }

        bump.QueueFree();
        runway.QueueFree();
        await Frames(2);
    }

    private async Task<List<VehicleState>> HandlingProbe(bool network, Vector3 position, Basis facing, Vector3 velocity, int ticks, Func<ulong, InputFrame> source)
    {
        if (!network)
        {
            return await Probe(position, facing, velocity, ticks, source);
        }

        _advance = false;
        _vehicle.CollisionLayer = 0;
        try
        {
            return await NetworkProbe(position, facing, velocity, ticks, source);
        }
        finally
        {
            _vehicle.CollisionLayer = 1;
            _advance = true;
        }
    }
}
