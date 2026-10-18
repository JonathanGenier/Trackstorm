using Godot;
using Trackstorm.Client.Items;

namespace Trackstorm.Client.Verification;

public sealed partial class DeveloperOptionsIntegrationChecks
{
    private void CheckWrenchRuntimeTuning(WrenchRepairSettings settings)
    {
        var arm = new WrenchArm();
        AddChild(arm);
        const float step = 1f / 120;
        for (int i = 0; i < 120 && !arm.IsReady; i++) { arm.Present(true, step); }
        Check(Math.Abs(arm.ReadySeconds - settings.DeploymentDuration * .75f) <= step + .0001,
            "Deployment tuning changes actual arm travel time");
        Check(Math.Abs(arm.RackDuration(true) - settings.DeploymentDuration * .25f) < .001f &&
            Math.Abs(arm.RackDuration(false) - settings.RetractionDuration * .25f) < .001f,
            "Travel tuning supplies the remaining rack/trunk phase budget");
        arm.BeginRepair();
        var chuck = (Node3D)arm.FindChild("Chuck", true, false);
        var contacts = new HashSet<int>();
        double started = arm.AnimationSeconds;
        int contactSamples = 0, hoverSamples = 0;
        for (int i = 0; i < 150 && arm.Repairing; i++)
        {
            Basis previousChuck = chuck.Basis;
            arm.Present(true, step);
            float phase = (float)(arm.AnimationSeconds - started) / settings.Duration * 3 - 1;
            if (phase is > .04f and < .14f)
            {
                float hover = WrenchArm.HoodContact(1).Y - settings.ContactForward * .13f / 1.18f + settings.HoverHeight;
                Check(Math.Abs(arm.Tip.Y - hover) < .001f, "Between-contact lift changes the actual hover height");
                hoverSamples++;
            }
            if (arm.ContactIndex < 0) { continue; }
            contactSamples++;
            contacts.Add(arm.ContactIndex);
            float elapsed = (float)(arm.AnimationSeconds - started);
            Vector3 expected = WrenchArm.HoodContact(arm.ContactIndex);
            expected.X = (arm.ContactIndex - 1) * settings.ContactSpacing;
            expected.Z -= settings.ContactForward;
            expected.Y -= settings.ContactForward * .13f / 1.18f;
            expected.Y += settings.Vibration * MathF.Abs(MathF.Sin(elapsed * settings.VibrationFrequency));
            Check(arm.Tip.DistanceTo(expected) < .001f, "Runtime drill respects tuned positions, sloped hood and vibration");
            Check(chuck.Basis.IsEqualApprox(new Basis(Vector3.Up, step * settings.DrillSpeed) * previousChuck), "Runtime chuck uses tuned drill speed");
        }
        Check(contacts.SetEquals(new[] { 0, 1, 2 }) && Math.Abs(arm.RepairCompletedSeconds - started - settings.Duration) <= step + .0001,
            "Tuned repair still performs exactly three contacts within its duration");
        Check(hoverSamples > 0 && Math.Abs(contactSamples * step - settings.Duration * settings.ContactFraction) < 3 * step,
            "Tuned contact hold changes actual drilling time while preserving raised travel");
        double returning = arm.AnimationSeconds;
        for (int i = 0; i < 120 && !arm.Nested; i++) { arm.Present(false, step); }
        Check(arm.Nested && arm.AnimationSeconds - returning <= settings.RetractionDuration * .75f + step,
            "Tuned return centers and folds before its rack phase");
        arm.QueueFree();

        var sparks = new WrenchSparks();
        AddChild(sparks);
        sparks.Burst(Vector3.Zero, settings);
        sparks.Advance(.08f);
        var flecks = sparks.GetChildren().OfType<MeshInstance3D>().Where(f => f.Visible).ToArray();
        Check(flecks.Length == 16 && flecks[0].Scale.IsEqualApprox(Vector3.One * settings.SparkSize),
            "Spark intensity and size affect actual visible meshes");
        Vector3 expectedFlight = Vector3.Up * .03f + new Vector3(3, 7, 0) * (.08f * settings.SparkSpeed) + Vector3.Down * (4 * .08f * .08f);
        Check(flecks[0].Position.DistanceTo(expectedFlight) < .001f, "Spark velocity tuning changes actual flight");
        sparks.Advance(settings.SparkLifetime - .081f);
        Check(sparks.Active, "Tuned sparks remain alive up to their lifetime");
        sparks.Advance(.002f);
        Check(!sparks.Active && sparks.GetChildren().OfType<MeshInstance3D>().All(f => !f.Visible), "Tuned sparks expire and hide all flecks");
        sparks.QueueFree();
    }
}
