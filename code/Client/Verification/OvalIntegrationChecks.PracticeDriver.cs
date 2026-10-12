using Godot;
using Trackstorm.Client.Vehicles;

namespace Trackstorm.Client.Verification;

public sealed partial class OvalIntegrationChecks
{
    private async Task VerifyPracticeDriver()
    {
        var viewport = new SubViewport { OwnWorld3D = true, Size = new(320, 180), RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled };
        AddChild(viewport);
        var practice = new VehicleArena { MovingTarget = true };
        viewport.AddChild(practice);
        _practice = practice;
        await Frames(600);
        var target = practice.Target;
        Vector3 previous = target.GlobalPosition;
        float travel = 0, minimum = float.MaxValue, maximum = 0, lane = 0;
        for (int frame = 0; frame < 9000; frame++)
        {
            await Frames(1);
            travel += target.GlobalPosition.DistanceTo(previous);
            previous = target.GlobalPosition;
            minimum = Math.Min(minimum, target.Snapshot.Speed);
            maximum = Math.Max(maximum, target.Snapshot.Speed);
            lane = Math.Max(lane, _centers.Min(point => HorizontalDistance(target.GlobalPosition, point)));
        }
        Check(travel > 2000 && lane < 4 && minimum > 12 && maximum < 16,
            $"Practice driver completes two laps: travel={travel:F2}m speed={minimum:F2}..{maximum:F2}m/s maximum lane error={lane:F2}m.");
        Check(target.DamageState.CurrentHP == target.DamageState.MaxHP, "Practice driver keeps full health without wall assistance.");
        Check(practice.Vehicles.Count(vehicle => vehicle.InputSource is not null) == 1, "Exactly one practice car receives autonomous driving input.");
        practice.ResetVehicles();
        await Frames(600);
        Check(target.Snapshot.Speed > 12, "Practice target resumes driving after ordinary arena reset.");
        _practice = null;
        viewport.QueueFree();
    }
}
