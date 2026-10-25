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
        string[] route = ["WinchExit", "CableRiser", "CablePlatformRear", "CableSwivel", "CableSwivelTop", "CableFairlead", "CableSpoonGuide", "CableEye"];
        float ropeLength = 0;
        for (int i = 0; i < route.Length - 1; i++)
        {
            var segment = Joint(i == 0 ? "Cable" : "CableSegment" + i);
            var mesh = segment.GetChildren().OfType<MeshInstance3D>().Single();
            var bounds = mesh.Mesh.GetAabb();
            Connected(mesh.ToGlobal(new(0, bounds.Position.Y, 0)), Joint(route[i]).GlobalPosition, "Cable start " + i);
            Connected(mesh.ToGlobal(new(0, bounds.End.Y, 0)), Joint(route[i + 1]).GlobalPosition, "Cable end " + i);
            float span = Joint(route[i]).GlobalPosition.DistanceTo(Joint(route[i + 1]).GlobalPosition);
            ropeLength += span;
            Require(bounds.Size.X > .06f && bounds.Size.Z > .06f, "Ship rope must retain readable physical thickness");
            var material = (StandardMaterial3D)mesh.GetSurfaceOverrideMaterial(0);
            Require(material.AlbedoTexture is not null && Math.Abs(material.Uv1Scale.Y * .12f - span) < .001f,
                "Authored rope braid must retain its physical pitch");
        }
        float drumAngle = (4 - ropeLength) / .18f;
        Require(Math.Abs(Mathf.AngleDifference(Joint("WinchDrum").Rotation.X, drumAngle)) < .002f,
            "Winch must rotate according to actual rope travel");
        Require(Math.Abs(Joint("WinchExit").Position.X - .052f * drumAngle / Mathf.Tau) < .001f,
            "Rope must leave the current winding tangent");
        Require(Math.Abs(Joint("WinchLevelWind").Position.X - Joint("WinchExit").Position.X) < .001f &&
            Math.Abs(Joint("WinchExit").Position.X) < .25f, "Level wind must follow the rope within the flanges");
        var localExit = Joint("WinchHousing").ToLocal(Joint("WinchExit").GlobalPosition);
        Require(Math.Abs(new Vector2(localExit.Y, localExit.Z).Length() - .18f) < .001f,
            "Rope must meet the wound drum, not float beside it");
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
        Require(Math.Abs(end.X) > .23f && Math.Abs(fairlead.X) > .23f && end.X * fairlead.X > 0 && Math.Abs(yaw.ToLocal(top.GlobalPosition).X) < .001f,
            "Underspoon cable must pass outside the spring wire");
        Vector3 terminal = Joint("ArmTip").ToLocal(Joint("CableEye").GlobalPosition);
        Vector3 springSeat = Joint("ArmTip").ToLocal(top.GlobalPosition);
        Require(terminal.Z < springSeat.Z - .30f && terminal.Y < springSeat.Y && terminal.Z > -.10f,
            "Terminal eye must attach beneath the spoon on the turret side of the spring, at the human-marked position");
    }
}
