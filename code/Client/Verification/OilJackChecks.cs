using System.Net;
using System.Net.Sockets;
using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Items;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;
using Trackstorm.Core.Networking.Transport;

namespace Trackstorm.Client.Verification;

/// <summary>Controlled native trajectories, production rack, real UDP and rendered Oil evidence.</summary>
public sealed partial class OilJackChecks : Node
{
    private readonly List<ReplicationTrafficGateway> _gateways = new();
    private readonly List<NetworkVehicleArena> _arenas = new();
    private readonly List<SubViewport> _views = new();
    private readonly List<string> _evidence = new();
    private readonly List<object> _trace = new();
    private int _stage;
    private int _frames;
    private int _boundary;
    private int _cycles;
    private ulong _id;
    private ulong _shooter = 1;
    private ulong _remoteFire;
    private OilPatch? _last;
    private ulong _fireEnd;
    private string _endpoint = "";
    private bool _done;
    private bool _wall;
    private bool _airborneFiring;
    private int _airborneEmitted;
    private ulong _expiration;
    private bool _hostFire;
    private int _grantFrame;
    private int _cameraHold;
    private Vector3 _cameraPosition;
    private Vector3 _cameraTarget;
    private string? _capture;
    private double _renderMs;
    private int _turnGrounded;
    private float _turnSlip;
    private int _finishFrame;
    private bool _deadlineObserved;
    private int _activeFrames;
    private bool _nestedGeometry;
    private float _readyFore;
    private float _readySide;
    private float _readyVertical;
    private static N.Vector3 Bank(float x, float z) => new N.Vector3(0,20,0) + N.Vector3.Transform(new N.Vector3(x,0,z), Rotation) + Normal * 1.4f;
    private readonly Dictionary<ulong, OilPatch[]> _publications = new();
    private static readonly N.Vector3 Normal = N.Vector3.Transform(N.Vector3.UnitY, N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitZ, .25f));
    private static readonly N.Quaternion Rotation = N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitZ, .25f);
    private bool _mouthClosing;
    private bool _mouthClosedBeforeRetract;
    private bool _fadeObserved;
    private static bool ProductionBank => OS.GetCmdlineUserArgs().Contains("--oil-production-bank");
    private static bool Clearance => OS.GetCmdlineUserArgs().Contains("--oil-clearance");
    private static bool GameplayCamera => OS.GetCmdlineUserArgs().Contains("--oil-gameplay-camera");
    private static string Output => ProjectSettings.GlobalizePath(Clearance ? "res://.godot/ts-245-round8/clearance" : GameplayCamera ? "res://.godot/ts-245-round8/gameplay" : ProductionBank ? "res://.godot/ts-245-round8/bank" : "res://.godot/ts-245-round8/runtime");

    public override void _Ready()
    {
        ProcessPriority = 1000;
        System.IO.Directory.CreateDirectory(Output);
        Engine.MaxFps = 60;
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        _endpoint = $"127.0.0.1:{((IPEndPoint)socket.Client.LocalEndPoint!).Port}";
        socket.Close();
        AddPeer(); AddPeer();
    }
    private void AddPeer()
    {
        int index = _arenas.Count;
        var gateway = new ReplicationTrafficGateway(); ulong server = 0;
        if (index == 0) { gateway.Listen(TransportEndpoint.DirectIp(_endpoint)); if (OS.GetCmdlineUserArgs().Contains("--oil-impaired")) { gateway.ConfigureSimulation(new NetworkSimulation(30, 5, 2, 0, 0)); } }
        else { server = gateway.Connect(TransportEndpoint.DirectIp(_endpoint)); }
        _gateways.Add(gateway);
        var view = new SubViewport { Size = new Vector2I(1280, 720), OwnWorld3D = true, RenderTargetUpdateMode = index == 0 ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled };
        if (index == 0) { var container = new SubViewportContainer(); AddChild(container); container.AddChild(view); } else { AddChild(view); }
        _views.Add(view);
        var arena = new NetworkVehicleArena { PrototypeMapForVerification = !ProductionBank, SpawnConfiguration = ProductionBank ? new() { PickupRadius = .01f } : null };
        arena.Initialize(gateway, index == 0 ? 245ul : 0, server);
        gateway.Received = message =>
        {
            var bytes = message.Payload.Span;
            if (bytes.Length >= 4 && bytes[0] == 'T' && bytes[1] == 'S' &&
                Trackstorm.Core.Networking.Replication.VehicleNetworkCodec.Kind(bytes) == Trackstorm.Core.Networking.Replication.VehicleNetworkCodec.Welcome)
            { GD.Print($"Oil admission peer {index}: Welcome received with connection={gateway.Connections.GetValueOrDefault(message.RemotePeerId)}, local={arena.Driver.LocalVehicleId}, rejected={arena.Driver.RejectedPackets}."); }
        };
        gateway.ConnectionChanged += change => GD.Print($"Oil connection peer {index}: {change.State}.");
        arena.Driver.ItemsReceived += publication =>
        {
            if (index == 0)
            {
                _publications[publication.World.Tick] = publication.Patches.ToArray();
                if (_publications.Count > 256) { _publications.Remove(_publications.Keys.Min()); }
            }
            else if (_publications.TryGetValue(publication.World.Tick, out var expected))
            { Check(publication.Patches.SequenceEqual(expected), "exact replicated Oil flight/geometry/deadlines"); }
        };
        view.AddChild(arena); _arenas.Add(arena);
        if (ProductionBank)
        {
            var inspection = new Camera3D { Name = "OilEvidenceCamera", Position = new(6, 27, 9) };
            arena.AddChild(inspection); inspection.LookAt(new(0, 23, 0)); inspection.MakeCurrent();
            return;
        }
        var bank = new StaticBody3D { Position = new(0, 20, 0), Rotation = new(0, 0, .25f), CollisionLayer = 1 };
        bank.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(100, 1, 100) } });
        bank.SetMeta("surface_identity", "Asphalt");
        bank.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new(100, 1, 100) }, MaterialOverride = MatchResourceLoader.LoadResource<Material>("res://assets/arena/materials/Asphalt.tres") }); arena.AddChild(bank);
        var bump = new StaticBody3D { Name = "OilFiringBump", Position = new Vector3(0, 20, 0) + Basis.FromEuler(new Vector3(0, 0, .25f)) * new Vector3(0, .60f, -42), Rotation = new(0, 0, .25f), CollisionLayer = 1 };
        bump.SetMeta("surface_identity", "Asphalt");
        bump.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(20, .20f, 1.2f) } });
        bump.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new(20, .20f, 1.2f) }, MaterialOverride = MatchResourceLoader.LoadResource<Material>("res://assets/arena/materials/Asphalt.tres") }); arena.AddChild(bump);
        var ramp = new StaticBody3D { Position = new(-30, 35, 15), Rotation = new(.4f, 0, 0), CollisionLayer = 1 };
        ramp.SetMeta("surface_identity", "Asphalt");
        ramp.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(8, 1, 12) } }); arena.AddChild(ramp);
        var wall = new StaticBody3D { Position = new(0, 25, 35), CollisionLayer = 1 };
        wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(30, 30, .5f) } });
        wall.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new(30, 30, .5f) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new(.3f,.28f,.25f) } }); arena.AddChild(wall);
        var camera = new Camera3D { Name = "OilEvidenceCamera", Position = new(6, 27, 9) }; arena.AddChild(camera); camera.LookAt(new(0, 23, 0)); camera.MakeCurrent();
    }
    public override void _PhysicsProcess(double delta)
    {
        if (_done) { return; }
        if (Clearance) { AdvanceClearance(); return; }
        if (ProductionBank) { AdvanceBank(); return; }
        try
        {
            _frames++;
            if (_clearanceFailureFrame > 0 && _frames > _clearanceFailureFrame + 4)
            { Check(false, $"moving stream entered box on {_liveClearanceIntersections} vertices"); }
            if (_finishFrame > 0 && _frames >= _finishFrame) { Finish(); return; }
            if (_frames - _boundary > 2600)
            {
                string peers = string.Join("; ", _arenas.Select((a, i) =>
                    $"peer {i}: local={a.Driver.LocalVehicleId}, tick={a.Driver.Latest?.Tick}, roster={a.Driver.Latest?.Vehicles.Count}, itemTick={a.Driver.ItemState?.World.Tick}, patches={a.Driver.ItemState?.Patches.Count}, rejected={a.Driver.RejectedPackets}, failure={a.Driver.Failure}"));
                throw new InvalidOperationException($"Oil jack stage {_stage} timeout: {peers}");
            }
            if (_stage == 3 && (_last?.Pour?.Emitted ?? 0) == 0) { Pose(new(0,40,-15), N.Quaternion.Identity, new(8,3,-10)); }
            foreach (var arena in _arenas)
            {
                bool braking = _stage == 1 && (_frames - _boundary is >= 145 and < 180 or >= 260 and < 285);
                var input = arena == _arenas[0] && _stage is 1 or 2
                    ? new Trackstorm.Core.Input.InputFrame(0, _stage == 2 ? (short)15000 : (short)0, braking ? (ushort)0 : (ushort)40000, braking ? ushort.MaxValue : (ushort)0,
                        _stage == 2 && _frames - _boundary is > 150 and < 210 ? Trackstorm.Core.Input.InputButtons.Drift : 0, 0, 0)
                    : default;
                arena.Advance(input); Check(arena.Driver.Failure.Length == 0, arena.Driver.Failure);
                if (arena == _arenas[0] && arena.Driver.Host is { } authority)
                {
                    _publications[authority.World.State.Tick] = authority.Items.Patches.ToArray();
                    if (_publications.Count > 512) { _publications.Remove(_publications.Keys.Min()); }
                }
            }
            if (_stage == 3 && _frames - _boundary == 100) { AddPeer(); _evidence.Add("Fresh peer joins during airborne firing and reconstructs active hardware/flight."); }
            var host = _arenas[0].Driver.Host!;
            if (host.World.State.Match?.Phase == MatchPhase.Active) { _activeFrames++; }
            if (_hostFire && _frames - _grantFrame >= (_stage == 1 ? 230 : 0) &&
                (_stage != 5 || DisplayServer.GetName() == "headless" || _completedCaptures.Contains("meter-full.png")))
            { Check(_arenas[0].Driver.RequestItemUse(), "ordinary Oil use queued/sent"); _hostFire = false; }
            if (_stage == 2)
            {
                var motion = host.World.GetVehicle(1).Movement;
                if (motion.Grounded) { _turnGrounded++; }
                _turnSlip = Math.Max(_turnSlip, Math.Abs(N.Vector3.Dot(motion.Physics.LinearVelocity, N.Vector3.Transform(N.Vector3.UnitX, motion.Physics.Orientation))));
            }
            if (_remoteFire != 0 && _arenas[1].Driver.LocalItem?.Active.Token == _remoteFire)
            { Check(_arenas[1].Driver.RequestItemUse(), "ordinary remote Oil wire request queued/sent"); _remoteFire = 0; }
            foreach (var p in host.Items.Patches)
            {
                Check(p.AdditionalSegments.Segments.Count <= 63 && (p.Pour?.Drops.Length ?? 0) <= 48, "bounded flight/footprint");
                if (_stage == 4 && (Math.Abs(p.Normal.Y) < .1f || p.AdditionalSegments.Segments.Any(s => Math.Abs(s.Normal.Y) < .1f))) { _wall = true; }
            }
            if (_stage == 6 && host.World.State.Tick < _expiration) { Check(host.Items.Patches.Any(p => p.Id == _id), "untouched hazard remains until captured 30-second deadline"); }
            if (_stage == 6 && host.World.State.Tick >= _expiration)
            {
                Check(!host.Items.Patches.Any(p => p.Id == _id), "whole hazard removed at deadline");
                if (!_deadlineObserved)
                {
                    Check(_last!.ExpiresAtTick - _last.Pour!.StartTick == 1800, "default deadline is exactly thirty seconds");
                    _evidence.Add($"Timer: activation {_last.Pour.StartTick}, deadline {_expiration}, first absent tick {host.World.State.Tick} (30 seconds at 60 Hz).");
                    _deadlineObserved = true;
                }
                foreach (var peer in _arenas.Skip(1))
                { if (peer.Driver.ItemState is { } state && state.World.Tick >= _expiration) { Check(!state.Patches.Any(p => p.Id == _id), "peer cleanup at accepted deadline"); } }
            }
            if (_frames % 10 == 0)
            {
                var p = host.Items.Patches.FirstOrDefault(p => p.Id == _id);
                var renderedJack = _arenas[0].Bodies.GetValueOrDefault(1ul)?.FindChildren("*", "", true, false).OfType<Items.OilJack>().FirstOrDefault();
                _trace.Add(new { stage = _stage, frame = _frames, tick = host.World.State.Tick, hazards = host.Items.Patches.Count,
                    headOffset = p?.Pour is { } motion ? new[] { motion.HeadOffset.X, motion.HeadOffset.Y, motion.HeadOffset.Z } : null,
                    emitted = p?.Pour?.Emitted, flight = p?.Pour?.Drops.Length, deposited = p?.Pour?.Deposited, segments = p?.AdditionalSegments.Segments.Count,
                    renderedHead = renderedJack is not null ? new[] { renderedJack.HeadOffset.X, renderedJack.HeadOffset.Y, renderedJack.HeadOffset.Z } : null,
                    mouthOpening = renderedJack?.MouthOpening, meterLevel = renderedJack?.MeterLevel, hoseError = renderedJack?.HoseEndpointError, extension = renderedJack?.Extension,
                    residues = _arenas[0].FindChildren("*", "", true, false).OfType<Items.OilHazardVisual>().Count(o => o.Retired), forcedRepeatRender = _stage == 7,
                    renderMs = _renderMs, grounded = host.World.GetVehicle(1).Movement.Grounded,
                    nodes = Performance.GetMonitor(Performance.Monitor.ObjectNodeCount), resources = Performance.GetMonitor(Performance.Monitor.ObjectResourceCount),
                    memory = Performance.GetMonitor(Performance.Monitor.MemoryStatic), drawCalls = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame),
                    rack = _arenas[0].Bodies.GetValueOrDefault(1ul)?.Rack.Progress, presented = _arenas[0].Bodies.GetValueOrDefault(1ul)?.Rack.PresentedItem.ToString() });
            }
            var current = host.Items.Patches.FirstOrDefault(p => p.Id == _id);
            var jack = _arenas[0].Bodies.GetValueOrDefault(1ul)?.FindChildren("*", "", true, false).OfType<Items.OilJack>().FirstOrDefault();
            if (jack is not null)
            {
                Check(jack.HeadOffset.Length() <= .451f, "bounded rendered spring displacement");
                if (jack.Extension >= .999f)
                {
                    Check(jack.ModelOffset == Vector3.Zero, "box stays directly on the original rack");
                    Vector3 localMouth = _arenas[0].Bodies[1].VisualTransform.AffineInverse() * jack.Mouth;
                    Check(localMouth.Z > 1.55f && localMouth.Y > 2.20f, "mouth remains above original rear hardware at extreme sway");
                    Check(jack.HoseEndpointError < .001f, "head hose remains attached during sway and kickback");
                    _readyFore = Math.Max(_readyFore, Math.Abs(jack.HeadOffset.Z)); _readySide = Math.Max(_readySide, Math.Abs(jack.HeadOffset.X)); _readyVertical = Math.Max(_readyVertical, Math.Abs(jack.HeadOffset.Y));
                }
                if (_stage == 5 && current is not null && jack.Nested)
                { Check(jack.FullyNestedGeometry, "head and bottom-anchored spring fit below the closed lid"); _nestedGeometry = true; }
                if (current?.Pour is { } mouthPour && host.World.State.Tick > mouthPour.StartTick + (ulong)mouthPour.DurationTicks && jack.MouthOpening > .001f)
                { Check(jack.Extension >= .999f, "mouth closes before head retraction"); _mouthClosing = true; }
                if (_mouthClosing && jack.Extension < .99f)
                { Check(jack.MouthOpening <= .001f, "head retracts only after mouth closure"); _mouthClosedBeforeRetract = true; }
            }
            if (_stage == 3 && current?.Pour is { } air && air.Emitted > _airborneEmitted)
            { _airborneFiring |= !host.World.GetVehicle(1).Movement.Grounded; _airborneEmitted = air.Emitted; }
            if (current is not null) { _last = current; _fireEnd = current.Pour!.StartTick + (ulong)current.Pour.DurationTicks; }
            if (_stage == 1 && _frames - _boundary == 35) { Capture("closed.png"); }
            if (_stage == 1 && _frames - _boundary == 125) { Capture("ready.png"); }
            if (_stage == 1 && _frames - _boundary == 105)
            {
                Capture("tank-detail.png");
                _cameraTarget = _arenas[0].Bodies[1].VisualTransform * new Vector3(0, 1.30f, 1.845f);
                _cameraPosition = _cameraTarget + new Vector3(.35f, .35f, 2.8f); _cameraHold = 3;
            }
            if (_stage == 1 && _frames - _boundary == 116)
            {
                Capture("jester-detail.png");
                _cameraTarget = _arenas[0].Bodies[1].Rack.OilMouth ?? _arenas[0].Bodies[1].GlobalPosition + new Vector3(0, 2.9f, 3.15f);
                _cameraPosition = _cameraTarget + new Vector3(1.1f, .3f, 1.9f); _cameraHold = 5;
            }
            if (_stage is >= 1 and <= 5 && current?.Pour is { } capturePour && host.World.State.Tick == capturePour.StartTick + 30) { Capture($"stage-{_stage}-firing.png"); }
            if (_stage == 1 && current?.Pour is { } impactPour && host.World.State.Tick == impactPour.StartTick + 55) { Capture("impact.png"); }
            if (_stage == 1 && current?.Pour is { } closePour && host.World.State.Tick == closePour.StartTick + 66) { Capture("mouth-closing.png"); }
            if (_stage == 6 && _deadlineObserved)
            {
                var residue = _arenas[0].FindChildren("*", "", true, false).OfType<Items.OilHazardVisual>().FirstOrDefault(o => o.HazardId == _id && o.Retired);
                if (residue is { FadeProgress: > .35f and < .7f } && !_fadeObserved)
                { _fadeObserved = true; Capture("expired-fading.png", true); }
            }
            if (_stage <= 5 && DisplayServer.GetName() != "headless" && _frames % 6 == 0 && _capture is null)
            { _capture = $"motion/frame-{_frames:D5}.png"; }
            if (_stage == 5 && jack is not null) { RecordMeter(jack); }
            switch (_stage)
            {
                case 0 when _arenas.All(a => a.Driver.Latest?.Vehicles.Count == 2) && _activeFrames > 120:
                    Check(Items.OilSurfaceQuery.Cast(_arenas[0].GetWorld3D().DirectSpaceState, new(0,25,35), new(0,25,36), .65f) is { Radius: 0 }, "embedded static impact retires without phantom deposition");
                    N.Vector3 rampNormal = N.Vector3.Transform(N.Vector3.UnitY, N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitX, .4f));
                    N.Vector3 rampPoint = new N.Vector3(-30,35,15) + rampNormal * .5f;
                    var rampHit = Items.OilSurfaceQuery.Cast(_arenas[0].GetWorld3D().DirectSpaceState, rampPoint + rampNormal * 3, rampPoint - rampNormal, 1.1f);
                    Check(rampHit is { Radius: > 1 } && N.Vector3.Dot(rampHit.Normal, rampNormal) > .99f, "native supported asphalt ramp impact");
                    _evidence.Add("Native 23-degree asphalt ramp sweep returns the supported impact radius and surface normal.");
                    if (OS.GetCmdlineUserArgs().Contains("--oil-query-only"))
                    {
                        GD.Print("Oil query safety passed."); _done = true;
                        foreach (var arena in _arenas) { arena.QueueFree(); } foreach (var gateway in _gateways) { gateway.Dispose(); }
                        GetTree().CreateTimer(.5).Timeout += () => GetTree().Quit(); break;
                    }
                    Pose(Bank(0,20), Rotation, new(0,0,-10)); Fire(); Next("Native banked straight firing with two real UDP peers."); break;
                case 1 when _frames - _boundary > 400:
                    Check(_last?.Pour is { Deposited: true, Emitted: 40, DurationTicks: 60 }, "one-second straight pour retains forty parcels");
                    _evidence.Add("One-second burst: captured 60 ticks, forty emitted parcels, one logical hazard; native acceleration/braking/reacceleration before firing.");
                    Check(N.Vector3.Dot(_last!.Normal, Normal) > .99f, "banked surface impact orientation");
                    Capture("straight-deposits.png", true); Pose(Bank(15,-10), Rotation, N.Vector3.Transform(new N.Vector3(0,0,-12), Rotation)); Fire(); Next("Native steering, throttle and handbrake while firing on the bank."); break;
                case 2 when _frames - _boundary > 330:
                    Check(_last!.AdditionalSegments.Segments.Count > 12, "curved multipart spill");
                    Check(_turnGrounded > 120, "turning car remains supported by native bank");
                    _evidence.Add($"Native turning: {_turnGrounded} supported ticks, peak slip {_turnSlip:F3}."); Capture("curved-deposits.png", true);
                    Pose(new(0,40,-15), N.Quaternion.Identity, new(8,3,-10)); Fire(); Next("Airborne activation with native gravity and landing."); break;
                case 3 when _frames - _boundary > 360:
                    Check(_airborneFiring, "native parcel emission while the vehicle has no wheel support");
                    Check(_last?.Pour is { Deposited: true, Emitted: 40 }, "airborne deposition completes"); Capture("airborne-deposits.png", true);
                    Pose(new(0,35,31), N.Quaternion.Identity, N.Vector3.Zero); Fire(); Next("Native first-hit deposition on a vertical world wall."); break;
                case 4 when _frames - _boundary > 360:
                    Check(_wall, "vertical surface deposition observed"); Capture("wall-deposits.png", true);
                    Pose(Bank(-20,-20), Rotation, N.Vector3.Zero); Fire(); Next("Replacement pickup while Oil owns the shared rack."); break;
                case 5:
                    if (current is not null && host.Items.Slots.Single(s=>s.Vehicle==1).Active.Item == HeldItem.None)
                    { Check(host.Items.Grant(host.World, 1, HeldItem.Wrench), "replacement acquired"); }
                    if (current is not null && host.World.State.Tick < _fireEnd)
                    { Check(_arenas[0].Bodies[1].Rack.PresentedItem != HeldItem.Wrench, "replacement cannot interrupt Oil firing"); }
                    if (_frames - _boundary > 420)
                    {
                        Check(_nestedGeometry, "fully retracted Oil geometry observed before replacement");
                        Check(_mouthClosedBeforeRetract, "open/vomit/close/retract sequence observed");
                        Check(_meterFull && _meterHalf && _meterEmpty && _meterRefilled && _meterPeerSamples > 10,
                            $"meter full={_meterFull}, halfway={_meterHalf}, empty={_meterEmpty}, refilled={_meterRefilled}, peer samples={_meterPeerSamples}");
                        _evidence.Add($"Visual meter: full before firing, halfway during accepted burst, empty after, full on replacement Oil; {_meterPeerSamples} samples verified against the observer's accepted clock; maximum publication lag {_meterPublicationLag} ticks, delayed level difference {_meterPeerDifference:F3}.");
                        Check(_readyFore > .08f && _readySide > .04f && _readyVertical > .01f, "visible spring response in all three axes");
                        _evidence.Add($"Rendered spring peaks: fore/aft {_readyFore:F3} m, lateral {_readySide:F3} m, vertical {_readyVertical:F3} m; full nesting observed before replacement.");
                        Check(_arenas[0].Bodies[1].Rack.PresentedItem == HeldItem.Wrench, "replacement appears after jester/box/rack return"); Capture("replacement-ready.png");
                        _expiration = _last!.ExpiresAtTick;
                        if (OS.GetCmdlineUserArgs().Contains("--oil-visual-only"))
                        { _finishFrame = _frames + 5; _stage = 9; break; }
                        AddPeer(); Pose(Bank(35,-35), Rotation, N.Vector3.Zero);
                        Next("Late admission, retained deadlines and 30-second cleanup.");
                    }
                    break;
                case 6 when _frames - _boundary > 1900 && _arenas.All(a=>a.Driver.ItemState?.Patches.Count==0):
                    Check(_fadeObserved, "visual-only two-second dissipation observed after expiry");
                    Check(_arenas.Count==4 && _arenas.All(a=>a.Driver.Latest?.Vehicles.Count==4), "late peer joined");
                    var slot=host.Items.Slots.Single(s=>s.Vehicle==1); Check(host.Items.Discard(host.World, 1, slot.Life, slot.Active.Token, slot.SelectionRevision), "clear fixture replacement");
                    Fire(); Next("Twelve repeated production activations and bounded cleanup."); break;
                case 7 when _frames - _boundary > 320:
                    _cycles++; Check(_last?.Pour is { Emitted: 40 }, "repeated activation completes");
                    if (_cycles < 12) { Fire(_cycles % 2 == 0 ? 1ul : 2ul); _boundary=_frames; }
                    else { Capture("repeated-use.png"); Next("Repeated use completed; wait for the final deadline."); } break;
                case 8 when _frames - _boundary > 1900 && _arenas.All(a=>a.Driver.ItemState?.Patches.Count==0):
                    Check(_deadlineObserved && _arenas.All(a => a.FindChildren("*", "", true, false).All(n => n is not Items.OilHazardVisual)), "all owned Oil visual nodes freed on four peers");
                    _evidence.Add("All hazards and owned visual/flight representations expire on four peers; no runtime exception was accepted.");
                    Finish(); break;
            }
        }
        catch(Exception e){ GD.PrintErr(e); foreach(var g in _gateways){g.Dispose();} GetTree().Quit(1); }
    }
    private void Fire(ulong shooter = 1)
    {
        var host=_arenas[0].Driver.Host!; _shooter = shooter; Check(host.Items.Grant(host.World,shooter,HeldItem.Oil),"Oil granted");
        var inventory=host.Items.Slots.Single(s=>s.Vehicle==shooter); _id=inventory.Active.Token; _last=null;
        if (shooter == 1) { _hostFire = true; _grantFrame = _frames; }
        else { _remoteFire = _id; }
    }
    private void Pose(N.Vector3 position,N.Quaternion orientation,N.Vector3 velocity)
    {
        var host=_arenas[0].Driver.Host!; var w=host.World.State; var pose=new VehiclePhysicsState(position,orientation,velocity,N.Vector3.Zero);
        host.World.Restore(new(w.Tick,w.LastInput,w.Vehicles.Select(v=>v.VehicleId!=1?v:new VehicleSnapshot(1,v.LifeId,new VehicleState(w.Tick,pose,true,false,0,0),v.Damage,pose)),w.Match));
        _arenas[0].Bodies[1].Apply(pose);
    }
    public override void _Process(double delta)
    {
        _renderMs = delta * 1000;
        if (_done || _arenas.Count == 0 || !_arenas[0].Bodies.TryGetValue(_shooter, out var car)) { return; }
        if (Clearance) { RenderClearance(); return; }
        if (!ProductionBank && _stage is >= 1 and <= 5 && _arenas[0].Bodies[1].FindChildren("*", "", true, false).OfType<Items.OilJack>().FirstOrDefault() is { } meterJack)
        {
            var host = _arenas[0].Driver.Host!;
            ObserveMeter(meterJack, host.Items.Patches.FirstOrDefault(p => p.Id == _id), host.World.State.Tick);
            ObserveLiveClearance(meterJack);
        }
        if (ProductionBank && car.FindChildren("*", "", true, false).OfType<Items.OilJack>().FirstOrDefault() is { } bankJack)
        { ObserveLiveClearance(bankJack); }
        var camera = _arenas[0].GetNode<Camera3D>("OilEvidenceCamera");
        if (GameplayCamera) { _arenas[0].GetNode<Camera3D>("ChaseCamera").MakeCurrent(); }
        else if (_meterCameraHold-- > 0) { AimMeter(camera, car); }
        else if (_cameraHold-- > 0) { camera.GlobalPosition = _cameraPosition; camera.LookAt(_cameraTarget); }
        else
        {
            camera.GlobalPosition = car.GlobalPosition + new Vector3(7, 4, _stage == 4 ? -8 : 9);
            camera.LookAt(car.GlobalPosition + new Vector3(0, 1.8f, _stage == 4 ? 1 : 3));
        }
        // Repeat-use performance must include rendered work even if Windows minimizes
        // this verification window. This is fixture instrumentation, not game behavior.
        if (_stage == 7 && DisplayServer.GetName() != "headless") { RenderingServer.ForceDraw(false); }
        if (_capture is not null)
        {
            string name = _capture; _capture = null;
            _ = ReadCapture(name);
        }
    }
    private async Task ReadCapture(string name)
    {
        try
        {
            // Drawing inside _Process can combine new world-space fluid vertices
            // with the previous rendered car pose. Let the complete scene finish
            // this frame before forcing readback at the next process boundary.
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (_done) { return; }
            var camera = _views[0].GetCamera3D();
            string path = System.IO.Path.Combine(Output, name);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            if (name.Contains("firing"))
            {
                var oil = _arenas[0].FindChildren("*", "", true, false).OfType<Items.OilHazardVisual>().LastOrDefault(o => !o.Retired);
                var liquid = oil?.FindChildren("*", "", true, false).OfType<Items.OilStreamVisual>().FirstOrDefault();
                if (liquid?.Mesh is ArrayMesh liquidMesh && liquidMesh.GetSurfaceCount() > 0)
                {
                    using var arrays = liquidMesh.SurfaceGetArrays(0);
                    Vector3 vertex = liquid.GlobalTransform * arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array()[0];
                    Vector3 mouth = _arenas[0].Bodies[_shooter].Rack.OilMouth!.Value;
                    GD.Print($"Readback {name}: mouth={camera.UnprojectPosition(mouth)}, stream={oil!.StreamMouth}, vertex={camera.UnprojectPosition(vertex)}, distance={vertex.DistanceTo(mouth):F3}");
                    Check(oil.StreamMouth.HasValue && vertex.DistanceTo(mouth) < .05f, "rendered liquid begins at the animated mouth during the active burst");
                }
            }
            RenderingServer.ForceSync();
            RenderingServer.ForceDraw(false);
            RenderingServer.ForceSync();
            RenderingServer.ForceDraw(false);
            if (name.StartsWith("meter-", StringComparison.Ordinal))
            { GD.Print($"Meter capture {name}: displayed level {_arenas[0].Bodies[1].FindChildren("*", "", true, false).OfType<Items.OilJack>().First().MeterLevel:F3}."); }
            using var image = _views[0].GetTexture().GetImage(); image.SavePng(path);
            _completedCaptures.Add(name);
        }
        catch (Exception error) { GD.PrintErr(error); foreach (var gateway in _gateways) { gateway.Dispose(); } GetTree().Quit(1); }
    }
    private void Capture(string name, bool deposits = false)
    {
        if (DisplayServer.GetName() == "headless") { return; }
        _capture = name;
        if (!deposits) { _cameraHold = 0; }
        if (name.Contains("firing"))
        {
            var stream = _arenas[0].FindChildren("*", "", true, false).OfType<Items.OilHazardVisual>().LastOrDefault();
            GD.Print($"Capture {name}: source={stream?.StreamMouth}, mouth={_arenas[0].Bodies[_shooter].Rack.OilMouth}, emitted={_last?.Pour?.Emitted}, tick={_arenas[0].Driver.Host!.World.State.Tick}, start={_last?.Pour?.StartTick}");
            if (_stage == 5)
            {
                _cameraTarget = _arenas[0].Bodies[1].Rack.OilMouth!.Value + Vector3.Down * 1.3f;
                _cameraPosition = _cameraTarget + new Vector3(3, 1, 5); _cameraHold = 3;
            }
        }
        if (deposits && _last is { } patch)
        {
            var positions = patch.AdditionalSegments.Segments.Select(s => Vehicles.VehicleBody.ToGodot(s.End)).Append(Vehicles.VehicleBody.ToGodot(patch.Position)).ToArray();
            Vector3 center = positions.Aggregate(Vector3.Zero, (a,b) => a+b) / positions.Length;
            float spread = positions.Max(p => p.DistanceTo(center));
            Vector3 normal = Vehicles.VehicleBody.ToGodot(patch.Normal);
            _cameraTarget = center;
            _cameraPosition = center + normal * Math.Max(6, spread * 1.6f) + (Math.Abs(normal.Y) > .5f ? Vector3.Back * 3 : Vector3.Up * 2);
            _cameraHold = 60;
        }
    }
    private void Finish()
    {
        if (!Clearance) { _evidence.Add($"Moving presentation clearance: {_liveClearanceFrames} rendered flight frames, {_liveClearanceIntersections} liquid vertices inside the box."); }
        System.IO.File.WriteAllLines(System.IO.Path.Combine(Output,"evidence.txt"),_evidence);
        System.IO.File.WriteAllText(System.IO.Path.Combine(Output,"trace.json"),System.Text.Json.JsonSerializer.Serialize(_trace));
        GD.Print("Oil jack integration passed: "+string.Join("\n",_evidence)); _done=true;
        foreach(var arena in _arenas){arena.QueueFree();} foreach(var g in _gateways){g.Dispose();}
        GetTree().CreateTimer(.5).Timeout += () => GetTree().Quit();
    }
    private void Next(string text){GD.Print(text);_evidence.Add(text);_stage++;_boundary=_frames;}
    private static void Check(bool value,string text){if(!value){throw new InvalidOperationException(text);}}
}
