using Godot;
using Trackstorm.Client.Items;
using Trackstorm.Core.Items;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Rendered production model, captive feeding and bounded lingering trail fixture.
/// Native multiplayer and authority are exercised separately by the Salvo/aim harnesses.</summary>
public sealed partial class SalvoProjectileChecks : Node3D
{
    private readonly List<string> _evidence = [];
    private readonly List<SalvoFlightVisual> _flights = [];
    private Camera3D _camera = null!;
    private string _output = string.Empty;

    public override void _Ready() => CallDeferred(MethodName.Run);

    public async void Run()
    {
        try
        {
            Engine.MaxFps = 60;
            _output = ProjectSettings.GlobalizePath("res://.godot/ts-212/projectiles");
            System.IO.Directory.CreateDirectory(_output);
            AddChild(new WorldEnvironment { Environment = new Godot.Environment
            { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new(.46f, .56f, .65f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White, AmbientLightEnergy = .8f } });
            AddChild(new DirectionalLight3D { RotationDegrees = new(-45, -30, 0), LightEnergy = 1.4f });
            _camera = new Camera3D { Position = new(4, 3, 5), Far = 10000 };
            AddChild(_camera); _camera.LookAt(new(0, 1, 0));
            AddChild(new MeshInstance3D { Position = new(0, -1, 0), Mesh = new PlaneMesh { Size = new(600, 600) },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new(.31f, .35f, .28f) } });
            await Captive();
            await FlightAndFade();
            await ConcurrentFlight();
            await Saturation();
            System.IO.File.WriteAllLines(System.IO.Path.Combine(_output, "evidence.txt"), _evidence);
            GD.Print($"Salvo projectile presentation passed: {_evidence.Count} checks; production round, fuse, tumble, 15-second fading smoke and fixed budget.");
            foreach (var child in GetChildren()) { child.QueueFree(); }
            await Frames(3);
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError(exception.ToString()); GetTree().Quit(1); }
    }

    private async Task Captive()
    {
        var catapult = new SalvoCatapult(); AddChild(catapult);
        _camera.Position = new(3, 2.8f, 3.7f); _camera.LookAt(new(0, .8f, .2f));
        for (int tick = 0; tick <= 75; tick++)
        {
            catapult.Present(tick / 75f, false, null, 1f / 60, 1);
            if (tick is 62 or 68 or 75) { await Capture("feed-" + tick); }
            await Frames(1);
        }
        Check(catapult.VisibleRounds == 5 && catapult.FuseLit && catapult.IsReady, "Five authored rounds; one lit captive round after cocking and feed.");
        Vector3 seated = catapult.Bowl.ToLocal(catapult.Rounds[4].GlobalPosition);
        foreach (float roll in new[] { 0f, 90, 180, 270 })
        {
            catapult.Position = new(2, 4, -3);
            catapult.RotationDegrees = new(20, 65, roll);
            catapult.Present(1, false, null, 35, 1);
            Check(catapult.Bowl.ToLocal(catapult.Rounds[4].GlobalPosition).DistanceTo(seated) < .001f && catapult.FuseLit,
                $"Seated round/fuse survive translated airborne pose and {roll}-degree roll, including 35-second ready wait.");
            _camera.Position = catapult.Position + new Vector3(3, 2.8f, 3.7f); _camera.LookAt(catapult.Position);
            await Capture("captive-roll-" + roll);
        }
        catapult.QueueFree(); await Frames(2);
        var closeup = SalvoFirework.Create(); AddChild(closeup);
        closeup.AddChild(new SalvoFuse { Position = SalvoFirework.FusePosition });
        _camera.Position = new(.7f, .45f, .8f); _camera.LookAt(Vector3.Zero);
        await Capture("paper-firework-closeup");
        closeup.QueueFree(); await Frames(2);
    }

    private async Task FlightAndFade()
    {
        var smoke = new SalvoSmokeTrail(); AddChild(smoke);
        var arc = new SalvoFlight(new(-18, 4, 0), new(18, 4, 0), 8, 90, 0, 1);
        _camera.Position = new(4, 18, 40); _camera.LookAt(new(0, 7, 0));
        for (int i = 0; i < 5; i++)
        { var flight = new SalvoFlightVisual { Smoke = smoke }; AddChild(flight); _flights.Add(flight); }
        Basis first = Basis.Identity;
        for (int tick = 1; tick <= 90; tick++)
        {
            for (int i = 0; i < _flights.Count; i++)
            {
                var shifted = arc with { Origin = arc.Origin + N.Vector3.UnitZ * (i - 2) * 2,
                    Target = arc.Target + N.Vector3.UnitZ * (i - 2) * 2, ElapsedTicks = tick };
                _flights[i].Observe(new((ulong)i + 1, 1, shifted.At(tick), shifted.VelocityAt(tick), 1800 - tick) { Arc = shifted });
            }
            if (tick == 1) { first = _flights[0].GetChild<Node3D>(0).Basis; }
            if (tick is 15 or 30 or 60 or 89) { await Capture("flight-" + tick); }
            await Frames(1);
        }
        Check(!first.IsEqualApprox(_flights[0].GetChild<Node3D>(0).Basis), "Flight model tumbles independently of ballistic translation.");
        Check(smoke.ActiveSegments > 250 && smoke.ActiveSegments < 1200, "Five simultaneous arcs leave connected bounded world-space trails.");
        foreach (var flight in _flights) { flight.QueueFree(); }
        _flights.Clear(); await Frames(2);
        smoke.SetProcess(false);
        int alive = smoke.ActiveSegments;
        await Capture("smoke-age-0");
        smoke.Advance(5); await Capture("smoke-age-5");
        Check(smoke.ActiveSegments == alive, "Smoke survives projectile removal and remains at five seconds.");
        smoke.Advance(5); await Capture("smoke-age-10");
        Check(smoke.ActiveSegments == alive, "Ten-second smoke remains alive while shader opacity gradually fades.");
        smoke.Advance(4); await Capture("smoke-age-14");
        smoke.Advance(1.1f); await Capture("smoke-age-15");
        Check(smoke.ActiveSegments == 0 && smoke.Multimesh.VisibleInstanceCount == 0, "All historical smoke expires fifteen seconds after its own emission.");
        smoke.QueueFree(); await Frames(2);
    }

    private async Task Saturation()
    {
        var smoke = new SalvoSmokeTrail(); AddChild(smoke); smoke.SetProcess(false);
        for (int tick = 0; tick < 1800; tick++)
        {
            for (int shot = 0; shot < 16; shot++)
            { smoke.Emit(new(tick * 3, shot, 0), new(tick * 3 + 3, shot, 0), (ulong)shot); }
            smoke.Advance(1f / 60);
        }
        Check(smoke.ActiveSegments <= 14416 && smoke.DroppedSegments == 0,
            "Sixteen simultaneous high-speed trails for thirty seconds stay within 16,384 segments without drops.");
        for (int i = 0; i < SalvoSmokeTrail.Capacity; i++) { smoke.Emit(Vector3.Zero, Vector3.Right, 1); }
        Check(smoke.ActiveSegments == SalvoSmokeTrail.Capacity && smoke.DroppedSegments > 0,
            "Over-budget catch-up declines new cosmetic segments while preserving existing fade lifetimes.");
        smoke.Advance(15.1f);
        Check(smoke.ActiveSegments == 0, "Saturated pool fully drains after emission stops.");
        smoke.QueueFree(); await Frames(2);
    }

    private async Task ConcurrentFlight()
    {
        var smoke = new SalvoSmokeTrail(); AddChild(smoke);
        var frames = new List<double>();
        _camera.Position = new(0, 30, 95); _camera.LookAt(new(0, 10, 0));
        for (int i = 0; i < 16; i++)
        { var flight = new SalvoFlightVisual { Smoke = smoke }; AddChild(flight); _flights.Add(flight); }
        for (int tick = 1; tick < 1800; tick++)
        {
            for (int i = 0; i < 16; i++)
            {
                // A slow accepted arc keeps all sixteen bodies and long trails in
                // view for the full lifetime, independently of normal balance.
                var arc = new SalvoFlight(new(-35, 4 + i % 4 * 2, i / 4 * 4), new(35, 4 + i % 4 * 2, i / 4 * 4), 12, 1800, tick, 1);
                _flights[i].Observe(new((ulong)i + 1, 1, arc.At(tick), arc.VelocityAt(tick), 1800 - tick) { Arc = arc });
            }
            await Frames(1);
            frames.Add(Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000);
            if (tick == 900) { await Capture("sixteen-concurrent-midflight"); }
        }
        Check(smoke.ActiveSegments <= SalvoSmokeTrail.Capacity && smoke.DroppedSegments == 0,
            $"Rendered sixteen concurrent rounds for thirty simulated seconds; bounded {smoke.ActiveSegments} smoke segments.");
        frames.Sort();
        _evidence.Add($"Native process time during rendered concurrent fixture: median={frames[frames.Count / 2]:F2} ms, p95={frames[(int)(frames.Count * .95)]:F2} ms; not a full-match GPU benchmark.");
        foreach (var flight in _flights) { flight.QueueFree(); }
        _flights.Clear(); smoke.QueueFree(); await Frames(2);
    }

    private async Task Frames(int count)
    { for (int i = 0; i < count; i++) { await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); } }

    private async Task Capture(string name)
    {
        if (DisplayServer.GetName() == "headless") { return; }
        _camera.ResetPhysicsInterpolation();
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        image.SavePng(System.IO.Path.Combine(_output, name + ".png"));
    }

    private void Check(bool condition, string message)
    { if (!condition) { throw new InvalidOperationException(message); } _evidence.Add(message); GD.Print(message); }
}
