using Godot;
using Trackstorm.Client.Items;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

public sealed partial class OilJackChecks
{
    private readonly HashSet<OilSegment> _bankChecked = new();
    private int _bankUnsafe, _bankVehicleHits, _bankImpacts, _bankGrounded, _bankAirborne;
    private float _bankSpeed, _bankSlip, _bankRise, _bankInitialHeight;
    private bool _bankReady;
    private Vector3 _bankPoint, _bankNormal;
    private Vector3 _grassPoint, _grassNormal, _entryForward;
    private float _bankTurn;
    private int _grassFrames, _asphaltFrames, _driftStart;

    private void AdvanceBank()
    {
        try
        {
            _frames++;
            if (_clearanceFailureFrame > 0 && _frames > _clearanceFailureFrame + 4)
            { Check(false, $"hard-drift stream entered box on {_liveClearanceIntersections} vertices"); }
            Check(_frames < 5000, "production bank verification timeout");
            var host = _arenas[0].Driver.Host!;
            foreach (var arena in _arenas)
            {
                bool drifting = _driftStart > 0 && _frames - _driftStart < 170;
                var input = arena == _arenas[0] && _bankReady && _stage > 0
                    ? new InputFrame(0, drifting ? (short)-32767 : (short)0, 40000, 0, drifting ? InputButtons.Drift : 0, 0, 0)
                    : default;
                arena.Advance(input); Check(arena.Driver.Failure.Length == 0, arena.Driver.Failure);
            }
            if (!_bankReady && _arenas.All(a => a.Driver.Latest?.Vehicles.Count == 2) && host.World.State.Match?.Phase == MatchPhase.Active)
            {
                _bankReady = true;
                FindBank();
                CheckVehicleSurface();
                CheckSurfaceEligibility();
                SeedBank(0); GrantBank();
                _evidence.Add("Production oval, two native UDP peers: approach from authored grass, cross onto bank asphalt, full-lock held drift and repeated/airborne Oil.");
            }
            if (!_bankReady) { return; }
            var body = _arenas[0].Bodies[1];
            if (_stage == 0 && body.Rack.IsOilReady(host.Items.Slots.Single(s => s.Vehicle == 1)))
            {
                SeedBank(_cycles);
                _stage = 1; _boundary = _frames; _driftStart = 0;
            }
            var pose = host.World.GetVehicle(1).Movement;
            Vector3 position = VehicleBody.ToGodot(pose.Physics.Position);
            using var groundRay = PhysicsRayQueryParameters3D.Create(position + Vector3.Up * 2, position - Vector3.Up * 12, 1);
            using var ground = _arenas[0].GetWorld3D().DirectSpaceState.IntersectRay(groundRay);
            var surface = ground.Count == 0 ? null : SurfaceIdentityResolver.Resolve(ground["collider"].AsGodotObject(), ground["position"].AsVector3());
            if (_stage > 0)
            {
                if (surface == SurfaceIdentity.Grass) { _grassFrames++; }
                if (surface == SurfaceIdentity.Asphalt) { _asphaltFrames++; }
                if (_driftStart == 0 && (surface == SurfaceIdentity.Asphalt || _cycles == 2 && _frames - _boundary > 20)) { _driftStart = _frames; }
                int fireDelay = _cycles switch { 0 => 24, 1 => 55, _ => 90 };
                if (_frames - _boundary == fireDelay) { Check(_arenas[0].Driver.RequestItemUse(), "grass-bank use accepted"); }
                var forward = VehicleBody.ToGodot(N.Vector3.Transform(-N.Vector3.UnitZ, pose.Physics.Orientation));
                float angle = new Vector2(_entryForward.X, _entryForward.Z).AngleTo(new(forward.X, forward.Z));
                _bankTurn = Math.Max(_bankTurn, Math.Abs(Mathf.RadToDeg(angle)));
            }
            _bankSpeed = Math.Max(_bankSpeed, pose.Physics.LinearVelocity.Length());
            _bankSlip = Math.Max(_bankSlip, Math.Abs(N.Vector3.Dot(pose.Physics.LinearVelocity, N.Vector3.Transform(N.Vector3.UnitX, pose.Physics.Orientation))));
            _bankRise = Math.Max(_bankRise, pose.Physics.Position.Y - _bankInitialHeight);
            if (pose.Grounded) { _bankGrounded++; } else { _bankAirborne++; }
            _last = host.Items.Patches.FirstOrDefault(p => p.Id == _id) ?? _last;
            foreach (var patch in host.Items.Patches.Where(p => p.Pour is { Deposited: true }))
            {
                CheckBankSegment(new(patch.Position, patch.Position, patch.Normal, patch.Radius));
                foreach (var segment in patch.AdditionalSegments.Segments) { CheckBankSegment(segment); }
            }
            if (_frames % 10 == 0)
            {
                _trace.Add(new { frame = _frames, cycle = _cycles, tick = host.World.State.Tick, stage = _stage,
                    position = new[] { pose.Physics.Position.X, pose.Physics.Position.Y, pose.Physics.Position.Z },
                    speed = pose.Physics.LinearVelocity.Length(), grounded = pose.Grounded, surface = surface?.ToString(), turn = _bankTurn, drifting = pose.Drifting, segments = _bankChecked.Count, unsupported = _bankUnsafe,
                    vehicleImpacts = _bankVehicleHits, emitted = _last?.Pour?.Emitted });
            }
            if (_stage == 1)
            {
                int elapsed = _frames - _boundary;
                if (DisplayServer.GetName() != "headless" && elapsed < 230 && elapsed % 6 == 0) { _capture = $"bank-motion/cycle-{_cycles}-frame-{elapsed:D3}.png"; }
                if (elapsed == (_cycles switch { 0 => 54, 1 => 85, _ => 120 })) { Capture($"bank-{_cycles}-firing.png"); }
                if (elapsed == 235) { Capture($"bank-{_cycles}-deposits.png", true); }
                if (elapsed > 300)
                {
                    Check(_last?.Pour?.Emitted == 40, "bank one-second activation retains forty parcels");
                    _evidence.Add($"Bank cycle {_cycles}: forty emitted; native accepted impacts {_bankImpacts}, vehicle proxy hits {_bankVehicleHits}, unsupported deposited pieces {_bankUnsafe}.");
                    if (++_cycles < 3) { _stage = 0; SeedBank(_cycles); GrantBank(); }
                    else
                    {
                        _evidence.Add($"Bank motion: speed {_bankSpeed:F3} m/s, lateral slip {_bankSlip:F3} m/s, rise {_bankRise:F3} m, heading change {_bankTurn:F2} degrees; grass {_grassFrames}, asphalt {_asphaltFrames}, supported {_bankGrounded}, airborne {_bankAirborne} frames; {_bankChecked.Count} pieces checked.");
                        Check(_bankSpeed > 25 && _bankSlip > 4 && _bankRise > 1 && _bankAirborne > 20 && _bankGrounded > 20, "bank drift/climb and airborne scenarios actually exercised");
                        Check(_grassFrames > 10 && _asphaltFrames > 50 && _bankTurn > 150, "grass-to-asphalt and near-180-degree hard drift actually exercised");
                        Check(_bankUnsafe == 0, "all deposited pieces retain real world support after cars move away");
                        Finish();
                    }
                }
            }
        }
        catch (Exception error)
        {
            System.IO.File.WriteAllText(System.IO.Path.Combine(Output, "trace.json"), System.Text.Json.JsonSerializer.Serialize(_trace));
            GD.PrintErr(error); foreach (var gateway in _gateways) { gateway.Dispose(); } GetTree().Quit(1);
        }
    }

    private void FindBank()
    {
        var shape = _arenas[0].Map.FindChildren("*", "CollisionShape3D", true, false).OfType<CollisionShape3D>()
            .Single(s => s.Shape.ResourcePath.EndsWith("/TrackCollision.tres", StringComparison.Ordinal));
        var vertices = ((ConcavePolygonShape3D)shape.Shape).Data;
        float best = float.MaxValue;
        for (int i = 0; i < vertices.Length; i += 3)
        {
            Vector3 point = shape.GlobalTransform * ((vertices[i] + vertices[i + 1] + vertices[i + 2]) / 3);
            Vector3 normal = (vertices[i + 1] - vertices[i]).Cross(vertices[i + 2] - vertices[i]).Normalized();
            if (normal.Y < 0) { normal = -normal; }
            float distance = point.DistanceSquaredTo(new(-196, 4, 0));
            if (distance < best && normal.Y < .9f) { best = distance; _bankPoint = point; _bankNormal = normal; }
        }
        Check(best < 200, "authored 35-degree oval bank located");
        GD.Print($"Bank start surface {_bankPoint}, normal {_bankNormal}.");
        best = float.MaxValue;
        for (int x = -185; x <= -160; x += 3)
        for (int z = -24; z <= 24; z += 3)
        {
            using var ray = PhysicsRayQueryParameters3D.Create(new(x, 30, z), new(x, -10, z), 1);
            using var hit = _arenas[0].GetWorld3D().DirectSpaceState.IntersectRay(ray);
            if (hit.Count == 0 || SurfaceIdentityResolver.Resolve(hit["collider"].AsGodotObject(), hit["position"].AsVector3()) != SurfaceIdentity.Grass) { continue; }
            Vector3 point = hit["position"].AsVector3();
            float distance = point.DistanceSquaredTo(_bankPoint + Vector3.Back * 10);
            if (distance < best) { best = distance; _grassPoint = point; _grassNormal = hit["normal"].AsVector3(); }
        }
        Check(best < 1500, "authored grass approach found near bank");
        GD.Print($"Grass approach {_grassPoint}, normal {_grassNormal}.");
        _arenas[0].Driver.OilCollision = (from, to, radius) =>
        {
            using var ray = PhysicsRayQueryParameters3D.Create(VehicleBody.ToGodot(from), VehicleBody.ToGodot(to), 1 | 2 | 16 | 32);
            ray.HitFromInside = true;
            using var raw = _arenas[0].GetWorld3D().DirectSpaceState.IntersectRay(ray);
            if (raw.Count > 0 && raw["collider"].AsGodotObject() is Networking.NetworkVehicleBody) { _bankVehicleHits++; }
            var hit = OilSurfaceQuery.Cast(_arenas[0].GetWorld3D().DirectSpaceState, from, to, radius);
            if (hit is { Radius: > 0 }) { _bankImpacts++; }
            return hit;
        };
    }

    private void SeedBank(int cycle)
    {
        Vector3 position = _grassPoint + _grassNormal * 1.2f;
        _entryForward = (_bankPoint + Vector3.Back * 35 - _grassPoint).Slide(_grassNormal).Normalized();
        Vector3 velocity = _entryForward * 32;
        if (cycle == 2) { position += Vector3.Up * 7; velocity += Vector3.Up * 5; }
        var rotation = Basis.LookingAt(_entryForward, _grassNormal).GetRotationQuaternion();
        Pose(VehicleBody.ToCore(position), new(rotation.X, rotation.Y, rotation.Z, rotation.W), VehicleBody.ToCore(velocity));
        _bankInitialHeight = position.Y;
    }

    private void GrantBank()
    {
        var host = _arenas[0].Driver.Host!;
        Check(host.Items.Grant(host.World, 1, HeldItem.Oil), "bank Oil granted");
        _id = host.Items.Slots.Single(s => s.Vehicle == 1).Active.Token; _last = null;
    }

    private void CheckVehicleSurface()
    {
        var car = _arenas[0].Bodies[1];
        Vector3 center = car.GlobalPosition;
        var hit = OilSurfaceQuery.Cast(_arenas[0].GetWorld3D().DirectSpaceState, VehicleBody.ToCore(center + Vector3.Up * 3), VehicleBody.ToCore(center), .5f);
        GD.Print($"Native vehicle-roof query: accepted radius={hit?.Radius}, position={hit?.Position}.");
        _evidence.Add($"Native car proxy roof query returned {(hit is null ? "no impact" : $"radius {hit.Radius}")}; no retained deposit is permitted.");
        Check(hit is null or { Radius: 0 }, "vehicle collision proxy cannot retain a world-space deposit");
    }

    private void CheckBankSegment(OilSegment segment)
    {
        if (!_bankChecked.Add(segment)) { return; }
        Vector3 normal = VehicleBody.ToGodot(segment.Normal), start = VehicleBody.ToGodot(segment.Start), end = VehicleBody.ToGodot(segment.End);
        Vector3 tangent = normal.Cross(Math.Abs(normal.Z) < .9f ? Vector3.Back : Vector3.Right).Normalized();
        bool supported = true;
        for (int i = -1; i < 64; i++)
        {
            float angle = i * Mathf.Tau / 64;
            Vector3 radial = tangent * Mathf.Cos(angle) + normal.Cross(tangent) * Mathf.Sin(angle);
            Vector3 point = i < 0 ? (start + end) / 2 : (radial.Dot(end - start) >= 0 ? end : start) + radial * segment.EdgeRadius(angle);
            using var ray = PhysicsRayQueryParameters3D.Create(point + normal * .06f, point - normal * .06f, 1);
            using var hit = _arenas[0].GetWorld3D().DirectSpaceState.IntersectRay(ray);
            if (hit.Count == 0 || Math.Abs((hit["position"].AsVector3() - point).Dot(normal)) > .025f)
            {
                if (supported) { GD.Print($"Unsupported perimeter sample {i}: point={point}, hit={(hit.Count == 0 ? "none" : hit["position"].AsVector3().ToString())}."); }
                supported = false;
            }
        }
        if (!supported)
        {
            _bankUnsafe++;
            GD.Print($"UNSUPPORTED Oil piece #{_bankUnsafe}: {segment}");
        }
    }

    private void CheckSurfaceEligibility()
    {
        var arena = _arenas[0];
        var bodies = new List<PhysicsBody3D>();
        void Sample(string name, PhysicsBody3D body, bool accepted)
        {
            body.Name = name; body.Position = new(bodies.Count * 6, 100, 0);
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(4, 1, 4) } });
            arena.AddChild(body); bodies.Add(body);
            var point = VehicleBody.ToCore(body.GlobalPosition);
            var hit = OilSurfaceQuery.Cast(arena.GetWorld3D().DirectSpaceState, point + N.Vector3.UnitY * 3, point, .7f);
            Check(accepted ? hit is { Radius: > .6f } : hit is { Radius: 0 }, "surface eligibility: " + name);
        }
        Sample("StableWorld", new StaticBody3D { CollisionLayer = 1 }, true);
        Sample("MovingStatic", new StaticBody3D { CollisionLayer = 1, ConstantLinearVelocity = Vector3.Right }, false);
        Sample("AnimatedPlatform", new AnimatableBody3D { CollisionLayer = 1 }, false);
        Sample("MovableWorldWall", new RigidBody3D { CollisionLayer = 32, Freeze = true }, false);
        Sample("MovableRockPiece", new StaticBody3D { CollisionLayer = 16 }, false);
        var rock = new StaticBody3D { CollisionLayer = 1 }; rock.SetMeta("environment_rock", 1);
        Sample("DestructibleRock", rock, false);
        var water = new StaticBody3D { CollisionLayer = 1 }; water.SetMeta("surface_identity", "Water");
        Sample("Water", water, false);
        Check(OilSurfaceQuery.Cast(arena.GetWorld3D().DirectSpaceState, new(0, 150, 0), new(0, 149, 0), .7f) is null, "missing surface remains ballistic");
        // A footprint at a ledge must shrink; it cannot invent supporting geometry.
        var edge = OilSurfaceQuery.Cast(arena.GetWorld3D().DirectSpaceState, new(1.8f, 103, 0), new(1.8f, 100, 0), .7f);
        Check(edge is { Radius: > 0 and < .2f }, "ledge footprint reduced to supported area");
        foreach (var body in bodies) { body.QueueFree(); }
        _evidence.Add("Native support eligibility passed: static terrain, ledge shrink, missing/water, cars, moving static/animated/rigid geometry and destructible rock proxies.");
    }
}
