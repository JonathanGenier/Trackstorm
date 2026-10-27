using Godot;
using Trackstorm.Client.Items;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;
using Trackstorm.Core.Matches;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

public sealed partial class OilJackChecks
{
    private OilStreamVisual? _clearanceStream;
    private OilFluidMaterial? _clearanceMaterial;
    private OilJack? _clearanceJack;
    private int _clearanceCases, _clearanceIntersections, _clearanceOccluded;
    private int _liveClearanceFrames, _liveClearanceIntersections, _clearanceFailureFrame;

    private void AdvanceClearance()
    {
        try
        {
            _frames++;
            Check(_frames < 1600, "clearance fixture timeout");
            foreach (var arena in _arenas) { arena.Advance(default); }
            var host = _arenas[0].Driver.Host!;
            if (_stage == 0 && _arenas.All(a => a.Driver.Latest?.Vehicles.Count == 2) && host.World.State.Match?.Phase == MatchPhase.Active)
            {
                Pose(new(0, 35, 0), N.Quaternion.Identity, N.Vector3.Zero);
                Check(host.Items.Grant(host.World, 1, HeldItem.Oil), "Oil granted for exact-boundary presentation exercise");
                _stage = 1;
            }
            if (_stage == 1 && _arenas[0].Bodies[1].Rack.IsOilReady(host.Items.Slots.Single(s => s.Vehicle == 1)))
            {
                _clearanceJack = _arenas[0].Bodies[1].FindChildren("*", "", true, false).OfType<OilJack>().Single();
                _clearanceMaterial = new OilFluidMaterial(true);
                _clearanceStream = new OilStreamVisual(_clearanceMaterial.Material);
                _arenas[0].AddChild(_clearanceStream);
                _stage = 2; _boundary = _frames;
            }
            if (_stage == 2)
            {
                // Fixed chassis isolates the entire permitted spring boundary, rather
                // than claiming an ordinary drive happened to hit every extreme.
                Pose(new(0, 35, 0), N.Quaternion.Identity, N.Vector3.Zero);
                if (_clearanceCases >= 780)
                {
                    _evidence.Add($"Spring boundary at .08/.24/.8 m stream widths: {_clearanceCases} rendered poses, {_clearanceIntersections} stream vertices inside box bounds, {_clearanceOccluded} quads occluded by hardware.");
                    System.IO.File.WriteAllLines(System.IO.Path.Combine(Output, "evidence.txt"), _evidence);
                    Check(_clearanceIntersections == 0, "emitted liquid clears the box across the entire permitted spring boundary");
                    _clearanceStream!.QueueFree(); _clearanceMaterial!.Dispose(); Finish();
                }
            }
        }
        catch (Exception e) { GD.PrintErr(e); foreach (var g in _gateways) { g.Dispose(); } GetTree().Quit(1); }
    }

    private void RenderClearance()
    {
        if (_stage != 2 || _clearanceJack is null || _clearanceStream is null) { return; }
        int phase = _clearanceCases % 260;
        float turn = Math.Max(0, phase - 25) * 2.399963f;
        float vertical = phase < 25 ? 0 : 1 - 2 * ((phase - 25) % 220) / 219f;
        float horizontal = MathF.Sqrt(Math.Max(0, 1 - vertical * vertical));
        var offset = phase < 25 ? new N.Vector3(0, 0, -.45f) : new N.Vector3(MathF.Sin(turn) * horizontal, vertical, MathF.Cos(turn) * horizontal) * .45f;
        var pour = new OilPour { Life = 1, StartTick = 1, HeadOffset = offset, Emitted = 20 };
        _clearanceJack.ReconstructPour();
        _clearanceJack.Present(true, .5f, pour, new(), 30);
        var car = _arenas[0].Bodies[1];
        Vector3 mouth = _clearanceJack.Mouth;
        N.Vector3 localMouth = VehicleBody.ToCore(car.VisualTransform.AffineInverse() * mouth);
        N.Vector3 velocity = OilJet.Velocity(localMouth, 18, new(0, -9.81f, 0), 0, 0);
        var drops = new List<OilDrop>();
        for (int i = 22; i >= 1; i--)
        {
            float t = i / 60f;
            var point = VehicleBody.ToCore(mouth) + velocity * t - N.Vector3.UnitY * (4.905f * t * t);
            drops.Add(new(point, velocity - N.Vector3.UnitY * (9.81f * t), 180 - i));
        }
        float width = (_clearanceCases / 260) switch { 0 => .08f, 1 => .24f, _ => .8f };
        _clearanceStream.Present(pour with { Drops = new OilFlight(drops) }, 0, mouth, Vector3.Zero, width, phase / 60.0, car.VisualTransform);
        _clearanceOccluded += _clearanceStream.OccludedQuads;
        if (_clearanceStream.Mesh is ArrayMesh mesh && mesh.GetSurfaceCount() > 0)
        {
            using var arrays = mesh.SurfaceGetArrays(0);
            foreach (var vertex in arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array())
            {
                Vector3 point = _clearanceJack.ToLocal(vertex);
                if (Math.Abs(point.X) < .46f && Math.Abs(point.Z) < .46f && point.Y is > -.04f and < .71f) { _clearanceIntersections++; }
            }
            _clearanceCases++;
        }
        var camera = _arenas[0].GetNode<Camera3D>("OilEvidenceCamera");
        camera.GlobalPosition = _clearanceJack.GlobalPosition + new Vector3(3.1f, 1.4f, 3.0f);
        camera.LookAt(_clearanceJack.GlobalPosition + new Vector3(0, .75f, .5f));
        if (DisplayServer.GetName() != "headless" && phase is 20 or 80 or 150 or 220)
        {
            RenderingServer.ForceSync(); RenderingServer.ForceDraw(false);
            using var image = _views[0].GetTexture().GetImage(); image.SavePng(System.IO.Path.Combine(Output, $"clearance-{width:F2}-{phase}.png"));
        }
    }

    private void ObserveLiveClearance(OilJack jack)
    {
        var oil = _arenas[0].FindChildren("*", "", true, false).OfType<OilHazardVisual>().FirstOrDefault(o => o.HazardId == _id && !o.Retired);
        var stream = oil?.FindChildren("*", "", true, false).OfType<OilStreamVisual>().SingleOrDefault();
        if (stream is not { Visible: true, Mesh: ArrayMesh mesh } || mesh.GetSurfaceCount() == 0 || jack.Extension < .999f) { return; }
        using var arrays = mesh.SurfaceGetArrays(0);
        var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        Transform3D transform = jack.GlobalTransform.AffineInverse() * stream.GlobalTransform;
        foreach (Vector3 vertex in vertices)
        {
            Vector3 point = transform * vertex;
            if (Math.Abs(point.X) < .43f && Math.Abs(point.Z) < .43f && point.Y is > 0 and < .70f)
            {
                _liveClearanceIntersections++;
                if (_clearanceFailureFrame == 0)
                {
                    _clearanceFailureFrame = _frames;
                    GD.Print($"Live stream/box intersection: stage={_stage}, point={point}, head={jack.HeadOffset}, mouth={jack.Mouth}, frame={_frames}.");
                    Capture("box-intersection.png");
                }
            }
        }
        _liveClearanceFrames++;
    }
}
