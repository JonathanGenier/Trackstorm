using Godot;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Vehicles;
using Numerics = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Exercises placed rock collision with the production car at native runtime.</summary>
public sealed partial class InfieldIntegrationChecks
{
    private async Task DressingImpacts(Node3D map)
    {
        if (_caseFilter.Length != 0 && !"DressingImpacts".StartsWith(_caseFilter, StringComparison.Ordinal))
        {
            return;
        }

        _drivenCases++;
        _drive = false;
        var landmarks = map.GetNode<Node3D>("EnvironmentDressing/RockLandmarks");
        var dressing = map.GetNode<Node3D>("EnvironmentDressing");
        Check(landmarks.GetChildCount() >= 3, "Three distinct production rock landmarks remain placed.");
        foreach (Node3D landmark in landmarks.GetChildren())
        {
            Vector3 center = landmark.GlobalPosition with { Y = 0 };
            foreach (float angle in new[] { -.25f, .25f })
            {
                Vector3 outward = (-center).Normalized().Rotated(Vector3.Up, angle);
                Vector3 start = center + outward * 12;
                start.Y = SurfaceHeight(start.X, start.Z) + .95f;
                Quaternion rotation = Basis.LookingAt(-outward).GetRotationQuaternion();
                _vehicle.ResetBody(new VehiclePhysicsState(VehicleBody.ToCore(start), new Numerics.Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W), VehicleBody.ToCore(-outward * 10), Numerics.Vector3.Zero));
                await Frames(3);
                float closest = 12;
                float peakSpeed = 0;
                float penetration = 12;
                var contacts = new HashSet<string>(StringComparer.Ordinal);
                bool finite = true;
                for (int frame = 0; frame < 150; frame++)
                {
                    await Frames(1);
                    Vector3 displacement = (_vehicle.Position with { Y = 0 }) - center;
                    closest = Math.Min(closest, displacement.Length());
                    penetration = Math.Min(penetration, displacement.Dot(outward));
                    peakSpeed = Math.Max(peakSpeed, _vehicle.LinearVelocity.Length());
                    foreach (Node3D body in _vehicle.GetCollidingBodies())
                    {
                        if (dressing.IsAncestorOf(body))
                        {
                            contacts.Add(body.GetPath().ToString());
                        }
                    }
                    finite &= _vehicle.Position.IsFinite() && _vehicle.LinearVelocity.IsFinite();
                }

                Check(finite && contacts.Count > 0 && penetration > 0 && peakSpeed < 18 && _vehicle.DamageState.CurrentHP < _vehicle.DamageState.MaxHP,
                    $"Dressing impact {landmark.Name}/{angle}: 10 m/s native approach contacts placed rocks without crossing landmark center; closest={closest:F2} m, approach-side distance={penetration:F2} m, peak speed={peakSpeed:F2} m/s, HP={_vehicle.DamageState.CurrentHP}; contacts={string.Join(',', contacts)}.");
                if (_camera is not null)
                {
                    await View($"Dressing-{landmark.Name}-{angle}", _vehicle.Position + outward * 10 + Vector3.Up * 5, landmark.GlobalPosition + Vector3.Up);
                }
            }
        }
    }
}
