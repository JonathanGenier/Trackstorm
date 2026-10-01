using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

public sealed partial class TerrainHandlingIntegrationChecks
{
    private async Task SurfaceDriving(bool network)
    {
        var normal = new List<float>();
        var braking = new List<float>();
        var sliding = new List<float>();
        var rotation = new List<float>();
        foreach (var material in new[] { SurfaceIdentity.Asphalt, SurfaceIdentity.Dirt, SurfaceIdentity.Grass })
        {
            foreach (string mode in new[] { "normal", "brake", "slide", "transitions" })
            {
                var result = await SurfaceTrial(network, material, mode);
                if (mode == "normal") { normal.Add(result.Speed); }
                if (mode == "brake") { braking.Add(result.Speed); }
                if (mode == "slide") { sliding.Add(result.Side); rotation.Add(result.Yaw); }
            }
            foreach (float grade in new[] { -20f, 20f })
                foreach (float yaw in new[] { 0f, MathF.PI / 2 })
                    await SurfaceTrial(network, material, "hold", grade, yaw);
        }
        Check(normal.All(speed => speed > 25), $"{network}: all three surfaces retain confident normal driving: {string.Join(", ", normal)} m/s");
        Check(braking[0] < braking[1] && braking[1] < braking[2], $"{network}: Asphalt > Dirt > Grass braking, residual speed: {string.Join(", ", braking)} m/s");
        Check(sliding[0] < sliding[1] && sliding[1] < sliding[2], $"{network}: Grass > Dirt > Asphalt deliberate slide, peak lateral speed: {string.Join(", ", sliding)} m/s");
        Check(rotation[0] < rotation[1] && rotation[1] < rotation[2], $"{network}: Grass > Dirt > Asphalt deliberate rotation, peak yaw: {string.Join(", ", rotation)} rad/s");
    }

    private async Task<(float Speed, float Side, float Yaw)> SurfaceTrial(bool network, SurfaceIdentity material, string mode, float grade = 0, float yaw = 0)
    {
        string name = $"{(network ? "network" : "practice")}-{material}-{mode}-{grade}-{yaw:F2}";
        var road = new StaticBody3D { RotationDegrees = new(grade, 0, 0), CollisionLayer = 1, CollisionMask = 2 };
        road.SetMeta("surface_identity", material.ToString());
        road.AddToGroup("landing_terrain");
        road.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(4000, 2, 4000) }, Position = new(0, -1, 0) });
        AddChild(road);
        await Frames(3);
        N.Vector3 normal = VehicleBody.ToCore(road.Basis.Y);
        var slope = N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitX, Mathf.DegToRad(grade));
        var orientation = N.Quaternion.Normalize(slope * N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitY, yaw));
        var forward = N.Vector3.Transform(-N.Vector3.UnitZ, orientation);
        var pose = new VehiclePhysicsState(normal * 1.145f, orientation, forward * (mode == "hold" ? 0 : 20), N.Vector3.Zero);
        _world = new(new Core.Simulation.SimulationConfiguration(60));
        var damage = new DamageConfiguration { MaxHP = 1000, CollisionScale = 5 };
        _world.AddVehicle(1, new(), damage, pose);
        if (network)
        {
            _network = new() { VehicleId = 1 };
            AddChild(_network);
            _network.Apply(pose);
        }
        else
        {
            _native = new() { Position = VehicleBody.ToGodot(pose.Position), Quaternion = new(orientation.X, orientation.Y, orientation.Z, orientation.W), DamageConfiguration = damage };
            _native.Initialize(_world);
            AddChild(_native);
            _native.ResetBody(pose);
        }
        int frames = mode switch { "normal" => 240, "brake" => 35, "slide" => 330, "hold" => 720, _ => 1800 };
        float peakSide = 0, peakYaw = 0, minimumUp = 1, finalSpeed = 0, maximumStep = 0;
        N.Vector3 holdStart = default, previousVelocity = pose.LinearVelocity;
        var trace = new List<object>();
        _advance = true;
        for (int frame = 0; frame < frames; frame++)
        {
            float steer = mode switch { "normal" => .15f, "brake" => .25f, "slide" => frame < 75 ? .6f : frame < 150 ? -.5f : 0, "transitions" => frame / 180 % 2 == 0 ? .08f : -.08f, _ => 0 };
            _steer = (short)(steer * short.MaxValue);
            _throttle = mode == "normal" ? (ushort)(.65f * ushort.MaxValue) : mode == "transitions" || (mode == "slide" && frame >= 75) ? (ushort)(.7f * ushort.MaxValue) : (ushort)0;
            _brake = mode == "brake" ? ushort.MaxValue : (ushort)0;
            _buttons = mode == "hold" || (mode == "slide" && frame < 75) ? InputButtons.Drift : 0;
            if (mode == "transitions" && frame % 90 == 0)
                road.SetMeta("surface_identity", new[] { "Asphalt", "Dirt", "Grass" }[frame / 90 % 3]);
            await Frames(1);
            var state = _world.GetVehicle(1).Movement;
            var physics = state.Physics;
            float side = N.Vector3.Dot(physics.LinearVelocity, N.Vector3.Transform(N.Vector3.UnitX, physics.Orientation));
            float turn = N.Vector3.Dot(physics.AngularVelocity, normal);
            minimumUp = Math.Min(minimumUp, N.Vector3.Dot(N.Vector3.Transform(N.Vector3.UnitY, physics.Orientation), normal));
            maximumStep = Math.Max(maximumStep, N.Vector3.Distance(previousVelocity, physics.LinearVelocity));
            previousVelocity = physics.LinearVelocity;
            if (mode != "slide" || frame < 75) { peakSide = Math.Max(peakSide, Math.Abs(side)); peakYaw = Math.Max(peakYaw, Math.Abs(turn)); }
            if (frame == 120) { holdStart = physics.Position; }
            finalSpeed = (physics.LinearVelocity - normal * N.Vector3.Dot(physics.LinearVelocity, normal)).Length();
            if (frame % 30 == 0) trace.Add(new { frame, speed = finalSpeed, side, yaw = turn, surface = state.CurrentSurface.ToString(), powerSlip = state.PowerSlip });
            if (state.PowerSlip != 0) { throw new InvalidOperationException(name + " generated automatic power slip"); }
        }
        var final = _world.GetVehicle(1);
        Check(final.Damage.CurrentHP == 1000 && final.Movement.Grounded && minimumUp > .9f, $"{name}: sustained supported, upright, undamaged driving (up {minimumUp:F4})");
        if (mode == "normal") Check(peakSide < 1, $"{name}: ordinary correction remains controlled, lateral {peakSide:F3} m/s");
        if (mode == "slide") Check(finalSpeed > 20 && Math.Abs(final.Movement.Physics.AngularVelocity.Y) < .02f, $"{name}: countersteer then throttle recovers {finalSpeed:F3} m/s with yaw {final.Movement.Physics.AngularVelocity.Y:F4}");
        if (mode == "transitions") Check(maximumStep < 2 && finalSpeed > 20, $"{name}: twenty material changes retain momentum, maximum step {maximumStep:F4} m/s, exit {finalSpeed:F3} m/s");
        if (mode == "hold")
        {
            var offset = final.Movement.Physics.Position - holdStart;
            float drift = (offset - normal * N.Vector3.Dot(offset, normal)).Length();
            Check(drift < .01f && finalSpeed < .001f, $"{name}: settled ten-second hold drift {drift:F7} m, speed {finalSpeed:F7} m/s");
            _buttons = 0;
            _throttle = ushort.MaxValue;
            await Frames(180);
            Check(_world.GetVehicle(1).Movement.CommandSpeed > 1, name + ": release restores driving");
        }
        System.IO.File.WriteAllText(System.IO.Path.Combine(_output, name + ".json"), System.Text.Json.JsonSerializer.Serialize(trace));
        _advance = false;
        _brake = 0;
        _native?.QueueFree(); _network?.QueueFree(); _native = null; _network = null;
        road.QueueFree();
        await Frames(3);
        return (finalSpeed, peakSide, peakYaw);
    }
}
