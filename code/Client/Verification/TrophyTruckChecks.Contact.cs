using Godot;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Isolates brief support loss from intentional aerial input at racing speed.</summary>
public sealed partial class TrophyTruckChecks
{
    private async Task HighSpeedContact(bool network)
    {
        foreach (float delay in new[] { 0.15f, 1f })
        foreach (bool gap in new[] { false, true })
        {
            var tuning = new VehicleConfiguration { AirDelay = delay };
            await Setup(network, $"contact-{(gap ? "gap" : "bump")}-{delay}",
                new(0, gap ? 2.4f : VehicleDimensions.RideHeight, 10), N.Quaternion.Identity, new(0, 0, -44.44f), tuning);
            _fixture.GetChildren().OfType<StaticBody3D>().Single(body => body is not Networking.NetworkVehicleBody).SetMeta("surface_identity", "Asphalt");
            if (!gap)
            {
                // A rounded 12 cm road bump, sampled independently by the four production rays.
                var faces = new List<Vector3>();
                for (int i = 0; i < 32; i++)
                {
                    float z0 = -2 + i / 8f, z1 = z0 + 0.125f;
                    float h0 = 0.12f * MathF.Pow(MathF.Cos(z0 * MathF.PI / 4), 2);
                    float h1 = 0.12f * MathF.Pow(MathF.Cos(z1 * MathF.PI / 4), 2);
                    Vector3 a = new(-10, h0, z0), b = new(10, h0, z0), c = new(10, h1, z1), d = new(-10, h1, z1);
                    faces.AddRange(new[] { a, b, c, a, c, d });
                }
                var bump = new StaticBody3D(); bump.SetMeta("surface_identity", "Asphalt"); bump.AddToGroup("landing_terrain");
                bump.AddChild(new CollisionShape3D { Shape = new ConcavePolygonShape3D { Data = faces.ToArray() } });
                _fixture.AddChild(bump);
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                using var ray = PhysicsRayQueryParameters3D.Create(new(0, 1, 0), new(0, -1, 0), 1);
                var hit = GetWorld3D().DirectSpaceState.IntersectRay(ray);
                Check(hit.Count > 0 && Math.Abs(hit["position"].AsVector3().Y - 0.12f) < 0.001f, "Rounded bump exposes its crest to production queries");
            }
            _pilot = (tick, _) => new(tick, 0, ushort.MaxValue, 0, 0, 0, 0);
            await Frames(240);
            var samples = _movementTrace;
            float minimumUp = samples.Min(s => N.Vector3.Transform(N.Vector3.UnitY, s.Physics.Orientation).Y);
            float air = samples.Max(s => s.Air.Seconds);
            int commanded = samples.Count(s => s.Air.Input != N.Vector3.Zero);
            GD.Print($"{_case}: maxAir={air:F3}, airCommandFrames={commanded}, minUp={minimumUp:F3}, maxY={samples.Max(s => s.Physics.Position.Y):F3}, crashTimer={samples.Max(s => s.CrashSeconds):F3}, maxPitchRate={samples.Max(s => Math.Abs(s.Physics.AngularVelocity.X)):F3}");
            if (delay == 1)
            {
                Check(commanded == 0, _case + " brief loss never commands intentional aerial input");
                Check(minimumUp > 0.9f && samples[^1].Grounded, _case + " stays wheel-down and regains support");
                Check(samples.All(s => s.CrashSeconds == 0), _case + " does not trigger crash assistance");
            }
            await Finish();
        }
    }
}
