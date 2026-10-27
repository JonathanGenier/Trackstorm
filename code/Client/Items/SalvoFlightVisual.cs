using Godot;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Items;

/// <summary>Reconstructs the accepted parabola and deterministic tumbling. Only reliable
/// membership retires the round; render-time extrapolation cannot explode or steer it.</summary>
internal sealed partial class SalvoFlightVisual : Node3D
{
    private readonly Node3D _model = SalvoFirework.Create();
    private readonly SalvoFuse _fuse = new() { Position = SalvoFirework.FusePosition };
    private SalvoFlight? _arc;
    private ulong _id;
    private float _age;
    private int _smokeTick;
    internal SalvoSmokeTrail? Smoke { get; set; }

    internal SalvoFlightVisual()
    {
        Name = "SalvoFirework";
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        AddChild(_model);
        _model.AddChild(_fuse);
    }

    internal void Observe(MissileState missile)
    {
        if (_arc is null) { _smokeTick = missile.Arc!.ElapsedTicks; }
        _arc = missile.Arc;
        _id = missile.Id;
        _age = 0;
        Present(_arc!.ElapsedTicks);
    }

    public override void _Process(double delta)
    {
        if (_arc is null) { return; }
        _age = Math.Min(.1f, _age + Math.Max(0, (float)delta));
        float tick = Math.Min(SalvoFlight.LifetimeTicks, _arc.ElapsedTicks + _age * 60);
        Present(tick);
        _fuse.Present((float)delta);
        // At most 60 segments per second per round, with six-step catch-up.
        // Old history is never replayed after reconstruction or a long outage.
        int smokeTick = (int)MathF.Floor(tick);
        if (smokeTick > _smokeTick)
        {
            int start = Math.Max(_smokeTick, smokeTick - 6);
            for (int from = start; from < smokeTick; from++)
            {
                Smoke?.Emit(VehicleBody.ToGodot(_arc.At(from)), VehicleBody.ToGodot(_arc.At(from + 1)), _id);
            }
            _smokeTick = smokeTick;
        }
    }

    private void Present(float tick)
    {
        Position = VehicleBody.ToGodot(_arc!.At(tick));
        float seconds = tick / 60;
        float seed = (_id % 97) * .37f;
        // Axial roll plus end-over-end tumble, independent of the flight direction.
        _model.Basis = new Basis(Vector3.Up, seed + seconds * 2.3f) *
            new Basis(Vector3.Forward, seconds * 7.7f) * new Basis(Vector3.Right, seconds * 11.3f);
    }
}
