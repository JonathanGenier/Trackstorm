using Godot;
using Trackstorm.Client.Items;

namespace Trackstorm.Client.Verification;

/// <summary>Checks the imported render geometry and its attachments across native motion.</summary>
internal static class CatapultConnectionAudit
{
    internal static void Verify(SalvoCatapult catapult)
    {
        Node3D Joint(string name) => (Node3D)catapult.FindChild(name, true, false);
        void Require(bool condition, string message)
        {
            if (!condition) { throw new InvalidOperationException(message); }
        }
        void Connected(Vector3 actual, Vector3 anchor, string label) =>
            Require(actual.DistanceTo(anchor) < .004f, label + " detached from its physical anchor");
        var coil = (MeshInstance3D)Joint("SpringCoil");
        Require(coil.IsVisibleInTree() && coil.Mesh is ArrayMesh array && array.GetBlendShapeCount() == 1, "Authored spring mesh/morph must be rendered");
        float length = .20f + 1.8f * coil.GetBlendShapeValue(0);
        Require(length is > .23f and < 1.99f, "Spring must have separated working coils within authored travel");
        Connected(coil.ToGlobal(Vector3.Zero), Joint("SpringFoot").GlobalPosition, "Lower rendered coil");
        Connected(coil.ToGlobal(Vector3.Up * length), Joint("SpringTop").GlobalPosition, "Upper rendered coil");
        Connected(Joint("SpringLowerSeat").GlobalPosition, Joint("SpringFoot").GlobalPosition, "Lower seat");
        Connected(Joint("SpringUpperSeat").GlobalPosition, Joint("SpringTop").GlobalPosition, "Upper seat");
        Require(Math.Abs(coil.GlobalBasis.X.Length() - 1) < .003f && Math.Abs(coil.GlobalBasis.Y.Length() - 1) < .003f,
            "Spring pitch must change without scaling its wire or seats");
        string[] route = ["WinchExit", "CableRiser", "CablePlatformRear", "CableSwivel", "CableSwivelTop", "CableFairlead", "CableEye"];
        for (int i = 0; i < route.Length - 1; i++)
        {
            var segment = Joint(i == 0 ? "Cable" : "CableSegment" + i);
            var mesh = segment.GetChildren().OfType<MeshInstance3D>().Single();
            var bounds = mesh.Mesh.GetAabb();
            Connected(mesh.ToGlobal(new(0, bounds.Position.Y, 0)), Joint(route[i]).GlobalPosition, "Cable start " + i);
            Connected(mesh.ToGlobal(new(0, bounds.End.Y, 0)), Joint(route[i + 1]).GlobalPosition, "Cable end " + i);
        }
        // Measure actual relative travel: the runner's rear end must remain within
        // the fixed guide, and each lifting post retains positive socket overlap.
        var lift = Joint("PlatformLift"); var slide = Joint("TurretSlide");
        Require(slide.Position.Z + 1.36f > -.67f && slide.Position.Z + 1.36f < .80f,
            "Sliding platform must remain engaged with its guide");
        Require(lift.Position.Y >= -.001f && lift.Position.Y <= .281f,
            "Platform lift exceeded its captured post travel");
        var yaw = Joint("TurretYaw"); var top = Joint("SpringTop");
        Vector3 end = yaw.ToLocal(Joint("CableEye").GlobalPosition);
        Vector3 fairlead = yaw.ToLocal(Joint("CableFairlead").GlobalPosition);
        Require(end.X > .19f && fairlead.X > .19f && Math.Abs(yaw.ToLocal(top.GlobalPosition).X) < .001f,
            "Underspoon cable must pass outside the spring wire");
    }
}
