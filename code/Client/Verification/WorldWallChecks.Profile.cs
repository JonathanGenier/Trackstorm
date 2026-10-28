using Godot;
using Trackstorm.Client.Items;

namespace Trackstorm.Client.Verification;

public sealed partial class WorldWallChecks
{
    private long _profileStart, _profileFits, _profileRays;
    private double _profileGround, _profilePhysics, _profileProcess;
    private int _profileFrames;

    public override void _Process(double delta)
    {
        if (!OS.GetCmdlineUserArgs().Contains("--world-wall-profile") || _done) { return; }
        ShieldGround.Profile = true;
        if (_profileStart == 0) { _profileStart = System.Diagnostics.Stopwatch.GetTimestamp(); }
        _profilePhysics += Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000;
        _profileProcess += Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000;
        if (++_profileFrames < 180) { return; }
        double seconds = System.Diagnostics.Stopwatch.GetElapsedTime(_profileStart).TotalSeconds;
        Record($"PROFILE walls={_arenas[0].Walls.Bodies.Count} fps={_profileFrames / seconds:F1} physics_ms={_profilePhysics / _profileFrames:F3} process_ms={_profileProcess / _profileFrames:F3} ground_ms/frame={(ShieldGround.Milliseconds - _profileGround) / _profileFrames:F3} fits={ShieldGround.Fits - _profileFits} rays={ShieldGround.Rays - _profileRays} nodes={Performance.GetMonitor(Performance.Monitor.ObjectNodeCount)} orphans={Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount)} bodies={Performance.GetMonitor(Performance.Monitor.Physics3DActiveObjects)}");
        _profileFrames = 0; _profileStart = System.Diagnostics.Stopwatch.GetTimestamp();
        _profilePhysics = 0; _profileProcess = 0;
        _profileGround = ShieldGround.Milliseconds; _profileFits = ShieldGround.Fits; _profileRays = ShieldGround.Rays;
    }
}
