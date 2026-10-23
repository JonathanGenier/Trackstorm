using Godot;
using Trackstorm.Client.Items;

namespace Trackstorm.Client.Verification;

public sealed partial class EmpClawChecks
{
    private void CheckLocalVfx()
    {
        var original = EmpClawVfxSettings.Current;
        var discharge = new EmpClawDischarge();
        var electric = new EmpClawElectric();
        var claw = new EmpClawVisual();
        AddChild(discharge); AddChild(electric); AddChild(claw);
        claw.SetProcess(false);
        try
        {
            EmpClawVfxSettings.Current = new();
            discharge.Fire(Transform3D.Identity); discharge.Advance(.1f);
            float radius = discharge.GetChildren().OfType<MeshInstance3D>().First().Scale.X;
            EmpClawVfxSettings.Current = new() { ShockwaveScale = 2 };
            discharge.Fire(Transform3D.Identity); discharge.Advance(.1f);
            Check(Math.Abs(discharge.GetChildren().OfType<MeshInstance3D>().First().Scale.X - radius * 2) < .001f,
                "Local shockwave control changes the actual ring mesh scale.");
            electric.Mode = EmpClawVisualState.Fired;
            electric.Advance(0);
            var ribbon = electric.GetChildren().OfType<MultiMeshInstance3D>().First().Multimesh;
            float end = ribbon.GetInstanceTransform(15).Origin.Z;
            EmpClawVfxSettings.Current = new() { TrailLength = 5 };
            electric.Advance(0);
            Check(ribbon.GetInstanceTransform(15).Origin.Z > end * 1.5f, "Local trail control changes actual flight ribbon reach.");
            EmpClawVfxSettings.Current = new() { ElectricIntensity = 0, ImpactIntensity = 0 };
            electric.Advance(0);
            Check(ribbon.VisibleInstanceCount == 0, "Zero intensity hides flight electricity.");
            electric.Mode = EmpClawVisualState.NoseDestroyed; electric.StateAge = .1f; electric.Advance(0);
            Check(electric.GetChildren().OfType<MultiMeshInstance3D>().All(m => m.Multimesh.VisibleInstanceCount == 0), "Zero impact intensity hides pop fragments.");
            EmpClawVfxSettings.Current = new() { DeflationSeconds = .8f, CleanupSeconds = 2 };
            claw.Observe(EmpClawVisualState.Activated, true);
            claw.Observe(EmpClawVisualState.Expired);
            claw.Advance(.4f);
            Check(claw.Inflation > .1f, "Longer deflation changes the actual balloon animation.");
            EmpClawVfxSettings.Current = new();
            claw.Advance(1);
            Check(!claw.Finished, "Release captures cleanup timing independently of later edits.");
            claw.Advance(.7f);
            Check(claw.Finished, "Captured cleanup ends and hides the model.");
        }
        finally
        {
            EmpClawVfxSettings.Current = original;
            discharge.QueueFree(); electric.QueueFree(); claw.QueueFree();
        }
    }
}
