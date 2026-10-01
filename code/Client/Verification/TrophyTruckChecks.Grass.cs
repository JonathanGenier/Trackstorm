using Godot;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

public sealed partial class TrophyTruckChecks
{
    private async Task GrassTransitions(bool network)
    {
        foreach (int repetition in new[] { 1, 2 })
        foreach (var route in new[] {
            ("straight-in-shallow", new N.Vector3(-65, 0, 91), -1.32f, 0f, 75),
            ("straight-in-sharp", new N.Vector3(-55, 0, 91), -1f, 0f, 60),
            ("straight-out-shallow", new N.Vector3(-65, 0, 80), -1.82f, 0f, 75),
            ("straight-out-sharp", new N.Vector3(-55, 0, 80), -2.1f, 0f, 42),
            ("bank-turn-in", new N.Vector3(100, 0, 92), -1.35f, -0.1f, 130),
            ("bank-turn-sharp", new N.Vector3(100, 0, 92), -1.35f, -0.25f, 100),
            ("bank-out", new N.Vector3(150, 0, 51), -2f, 0f, 50),
            ("bank-descend", new N.Vector3(140, 0, 81), 0.35f, 0f, 90),
            ("west-bank-turn-in", new N.Vector3(-100, 0, 92), 1.35f, 0.1f, 130),
            ("west-bank-turn-sharp", new N.Vector3(-100, 0, 92), 1.35f, 0.25f, 100),
            ("west-bank-out", new N.Vector3(-150, 0, 51), 2f, 0f, 50),
            ("west-bank-descend", new N.Vector3(-140, 0, 81), -0.35f, 0f, 90) })
        {
            await Setup(network, $"grass-{route.Item1}-{repetition}", route.Item2,
                N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitY, route.Item3), new(0, 0, -44.44f), productionMap: true, alignSurface: true);
            _pilot = (tick, _) => new(tick, (short)(route.Item4 * short.MaxValue), 45875, 0, 0, 0, 0);
            await Frames(route.Item5);
            float flight = _movementTrace.Max(s => s.Air.Seconds);
            GD.Print($"{_case}: max air={flight:F3}s compression={_maximumCompression:F3} min up={_minimumUp:F3} chassis={_chassisContact}");
            Check(_movementTrace.Any(s => s.CurrentSurface == SurfaceType.Asphalt) && _movementTrace.Any(s => s.CurrentSurface == SurfaceType.Grass), _case + " crosses both production materials");
            Check(flight < 0.2f && _minimumUp > 0.75f, _case + " stays wheel-down without sustained transition launch");
            Check(_movementTrace.All(s => s.Air.Input == N.Vector3.Zero && s.CrashSeconds == 0), _case + " does not invoke aerial/crash assistance");
            await Finish();
        }
        _map!.QueueFree(); _map = null;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
