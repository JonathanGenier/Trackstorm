using Godot;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

public sealed partial class WorldWallChecks
{
    private bool Heavy => OS.GetCmdlineUserArgs().Contains("--world-wall-heavy");
    private readonly Dictionary<ulong, (float HP, ulong Sequence, ulong Tick)> _heavyDamage = new();
    private N.Vector3 _heavyStart;
    private float _heavyDrift;

    private void AdvanceHeavy()
    {
        var host = _arenas[0].Driver.Host!;
        int elapsed = _frame - _boundary;
        Check(elapsed < 1200, $"Heavy stage {_stage}/{_scenario} timeout");
        for (int i = 0; i < _arenas.Count; i++)
        {
            bool use = _stage == 2 && elapsed == 1 && i == 0;
            ushort throttle = _stage == 4 && _scenario == 7 && i == 1 ? ushort.MaxValue : (ushort)0;
            _arenas[i].Advance(new((ulong)_frame, 0, throttle, 0, use ? InputButtons.UseItem : 0, use ? InputButtons.UseItem : 0, 0));
            Check(_arenas[i].Driver.Failure.Length == 0, _arenas[i].Driver.Failure);
        }
        _boundaries[host.World.State.Tick] = host.Items.Shields.ToArray();
        if (_stage == 4)
        {
            var wall = host.Items.Shields.Single();
            Check(!wall.Tipping && N.Vector3.Transform(N.Vector3.UnitY, wall.Orientation).Y > .98f, "Ordinary heavy impacts stay upright");
            _heavyDrift = Math.Max(_heavyDrift, N.Vector3.Distance(wall.Position, _heavyStart));
            foreach (var car in host.World.State.Vehicles)
            {
                var previous = _heavyDamage[car.VehicleId];
                if (car.Damage.LastDamage is { } hit && hit.Sequence != previous.Sequence)
                {
                    Check(previous.Tick == 0 || hit.Tick - previous.Tick >= host.Configuration.Configuration.Damage.CollisionCooldownTicks, "No rapid duplicate damage ticks");
                    _heavyDamage[car.VehicleId] = (previous.HP, hit.Sequence, hit.Tick);
                }
                Check(car.ObservedPhysics.LinearVelocity.Y < 4, $"Heavy contact cannot launch a vehicle: case {_scenario}, car {car.VehicleId}, pose {car.ObservedPhysics}");
            }
        }
        switch (_stage)
        {
            case 0 when _arenas.All(a => a.Driver.Latest?.Vehicles.Count == 2):
                Check(host.TryConfigure(0, new Dictionary<string, double> { ["match.countdown_ticks"] = 1, ["match.minimum_players"] = 1 }, out _), "Heavy fixture config");
                Next(1); break;
            case 1 when host.World.State.Match?.Phase == Core.Matches.MatchPhase.Active:
                foreach (var wall in host.Items.Shields.ToArray()) { host.Items.DamageShield(host.World, wall.Id, wall.DamageSequence + 1, 1000, new("world", 0, "fixture-reset")); }
                foreach (ulong id in new[] { 1ul, 2ul }) { host.Items.RemovePlayer(id); Position(id, new(25, 201.65f, 30 + id * 10), N.Vector3.Zero); }
                Position(1, new(_scenario == 5 ? 100 : 0, 201.65f, 0), N.Vector3.Zero);
                Check(host.Items.Grant(host.World, 1, HeldItem.Shield), "Repeated heavy grant");
                Next(10); break;
            case 10 when elapsed > 100 && _arenas[0].Driver.LocalItem is { } item && _arenas[0].Bodies[1].CanDeployShield(item):
                Position(1, new(_scenario == 5 ? 100 : 0, 201.65f, 0), N.Vector3.Zero);
                Next(2); break;
            case 2 when elapsed > 90:
                Check(host.Items.Shields.Count == 1 && !host.Items.Shields[0].Attached, "Ordinary deployment creates one heavy wall");
                Position(1, new(25, 201.65f, 50), N.Vector3.Zero);
                Next(3); break;
            case 3 when elapsed > 30:
                var target = host.Items.Shields.Single();
                Check(target.WallMass == 18000, "Production heavy mass captured");
                _heavyStart = target.Position; _heavyDrift = 0; _heavyDamage.Clear();
                foreach (var car in host.World.State.Vehicles) { _heavyDamage[car.VehicleId] = (car.Damage.CurrentHP, car.Damage.LastDamage?.Sequence ?? 0, 0); }
                var normal = _scenario switch { 3 => new Vector3(-1, 0, -1).Normalized(), 4 => new Vector3(1, 0, -1).Normalized(), _ => Vector3.Back };
                var point = VehicleBody.ToGodot(target.Position) + (_scenario is 3 or 4 ? new Vector3(_scenario == 3 ? -2.7f : 2.7f, 0, -.8f) : Vector3.Zero);
                float speed = _scenario is 0 or 7 ? 3 : _scenario == 2 ? 40 : 18;
                // The slow case starts close enough to reach contact while coasting.
                HeavyCar(2, point + normal * (speed == 3 ? 3.1f : 8), -normal * speed);
                if (_scenario == 6) { HeavyCar(1, point - normal * 8, normal * 18); }
                var camera = _arenas[0].GetNode<Camera3D>("WallCamera");
                camera.Position = VehicleBody.ToGodot(target.Position) + new Vector3(16, 10, 16); camera.LookAt(VehicleBody.ToGodot(target.Position));
                Next(4); break;
            case 4 when elapsed >= 300:
                var result = host.Items.Shields.Single();
                float damage = _heavyDamage[2].HP - host.World.GetVehicle(2).Damage.CurrentHP;
                Record($"Heavy case {_scenario}: drift {_heavyDrift:F3} m, car damage {damage:F1}, wall HP {result.HP:F1}, final speed {result.LinearVelocity.Length():F3} m/s.");
                if (_scenario == 0) { Check(_heavyDrift < .4f && damage == 0, "Casual contact barely moves heavy wall and remains below damage threshold"); }
                else if (_scenario == 6)
                { Check(damage > 0 && _heavyDamage[1].HP > host.World.GetVehicle(1).Damage.CurrentHP && _heavyDrift < .4f, "Opposing simultaneous impacts damage both cars and largely cancel displacement"); }
                else if (_scenario == 7) { Check(damage == 0 && _heavyDrift < 3, "Sustained engine contact remains heavy and cannot tick damage"); }
                else { Check(damage > 0 && _heavyDrift > .05f, "Center/wing/bank impact damages vehicle and physically moves wall"); }
                Check(result.LinearVelocity.Length() < (_scenario == 7 ? 2 : .2f), "Heavy wall settles after impact");
                VerifyBoundaries();
                foreach (var arena in _arenas.Skip(1))
                foreach (var car in arena.Driver.ItemState!.World.Vehicles)
                { Check(car.State.Damage.CurrentHP == host.World.GetVehicle(car.State.VehicleId).Damage.CurrentHP, "Peer vehicle damage converges"); }
                Capture($"heavy-{_scenario}.png");
                if (++_scenario < 8) { Next(1); break; }
                System.IO.File.WriteAllLines(System.IO.Path.Combine(_output, "heavy-evidence.txt"), _evidence);
                GD.Print("World wall integration passed: heavy center/wings/bank, simultaneous cars, repeated use, sustained push and authoritative damage.");
                Finish(0); break;
        }
    }

    private void HeavyCar(ulong id, Vector3 point, Vector3 velocity)
    {
        using var ray = PhysicsRayQueryParameters3D.Create(point + Vector3.Up * 8, point - Vector3.Up * 8, 1);
        using var hit = _arenas[0].GetWorld3D().DirectSpaceState.IntersectRay(ray);
        Check(hit.Count > 0, "Heavy car has native terrain support");
        var up = hit["normal"].AsVector3();
        var back = -velocity.Normalized(); back.Y = -(back.X * up.X + back.Z * up.Z) / up.Y; back = back.Normalized();
        var rotation = new Basis(up.Cross(back).Normalized(), up, back).GetRotationQuaternion();
        Position(id, VehicleBody.ToCore(hit["position"].AsVector3() + Vector3.Up * (Core.Vehicles.VehicleDimensions.RideHeight / up.Y)),
            VehicleBody.ToCore(velocity), new(rotation.X, rotation.Y, rotation.Z, rotation.W));
    }
}
