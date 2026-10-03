namespace Trackstorm.Core.Vehicles;

/// <summary>Shared reversible trunk-then-rack mechanical path; retuning preserves progress.</summary>
public static class RackDeploymentPath
{
    /// <summary>Consumes elapsed seconds across both phases using current configured speeds.</summary>
    public static float Advance(float progress, bool deployed, float seconds, VehicleConfiguration tuning)
    {
        bool trunk = deployed ? progress < .45f : progress <= .45f;
        float boundary = deployed ? (trunk ? .45f : 1) : (trunk ? 0 : .45f);
        float rate = (trunk ? tuning.TrunkDeploymentSpeed : tuning.RackDeploymentSpeed) / 1.6f;
        float step = Math.Min(seconds, Math.Abs(boundary - progress) / rate);
        progress += Math.Sign(boundary - progress) * step * rate;
        seconds -= step;
        if (seconds > 0)
        {
            rate = (trunk ? tuning.RackDeploymentSpeed : tuning.TrunkDeploymentSpeed) / 1.6f;
            float target = deployed ? 1 : 0;
            progress += Math.Sign(target - progress) * Math.Min(Math.Abs(target - progress), seconds * rate);
        }
        return Math.Clamp(progress, 0, 1);
    }
}
