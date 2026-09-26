using Godot;
using Trackstorm.Client.Arenas;
using Trackstorm.Client.Hud;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Native finite perimeter impacts, genuine ballistic escape, OOB lifecycle and rendered feedback.</summary>
public sealed partial class BoundaryIntegrationChecks : Node3D
{
    private Core.Simulation.Simulation _world = null!;
    private VehicleBody? _native;
    private NetworkVehicleBody? _network;
    private Node3D _map = null!;
    private Camera3D? _camera;
    private CombatHud _hud = null!;
    private bool _advance;
    private readonly List<VehicleSnapshot> _states = new();
    private readonly List<string> _lines = new();
    private string _output = "";

    public override void _Ready() => CallDeferred(MethodName.Run);
    public override void _PhysicsProcess(double delta)
    {
        if (!_advance) return;
        var input = new InputFrame(_world.State.Tick + 1,0,0,0,0,0,0);
        var request = _native is not null ? _native.Capture(input) : new VehicleStepRequest(1,input,_network!.Observe(_world.GetVehicle(1)));
        var result = _world.Step(input,[request])[0];
        if (_native is not null) { _native.Apply(result); _native.Publish(); }
        else { _network!.Apply(result.Snapshot); }
        _states.Add(result.Snapshot);
    }

    private async void Run()
    {
        try
        {
            _output = ProjectSettings.GlobalizePath("res://.godot/boundary-checks");
            System.IO.Directory.CreateDirectory(_output);
            _map = ActiveMap.Load(); AddChild(_map);
            _hud = new CombatHud { Vehicle = () => _world?.GetVehicle(1) }; AddChild(_hud);
            if(DisplayServer.GetName() != "headless")
            {
                AddChild(new WorldEnvironment { Environment=GD.Load<Godot.Environment>("res://assets/maps/oval/Daylight.tres") });
                AddChild(new DirectionalLight3D { RotationDegrees=new(-55,-25,0),LightEnergy=1.4f });
                _camera = new Camera3D { Current=true,Far=1500 }; AddChild(_camera);
            }
            await Frames(4);
            using var json = System.Text.Json.JsonDocument.Parse(Godot.FileAccess.GetFileAsString("res://assets/maps/oval/measurements.json"));
            var sections = json.RootElement.GetProperty("sections_godot").EnumerateArray().ToArray();
            Vector3 Vec(System.Text.Json.JsonElement a) => new(a[0].GetSingle(),a[1].GetSingle(),a[2].GetSingle());
            Node wall = _map.GetNode("MapContent/PhysicalPerimeter");
            int rays=0;
            for(int i=0;i<sections.Length;i++)
            {
                int j=(i+1)%sections.Length;
                foreach(float t in new[]{0f,.5f,.999f})
                {
                    Vector3 rim=Vec(sections[i][1]).Lerp(Vec(sections[j][1]),t);
                    Vector3 inside=Vec(sections[i][0]).Lerp(Vec(sections[j][0]),t);
                    Vector3 direction=(rim-inside) with { Y=0 }; direction=direction.Normalized();
                    foreach(float height in new[]{-1f,.6f,3f,6f,8f,30f})
                    {
                        using var query=PhysicsRayQueryParameters3D.Create(rim+Vector3.Up*height+direction*1.4f,rim+Vector3.Up*height-direction*3,1);
                        var hit=GetWorld3D().DirectSpaceState.IntersectRay(query);
                        bool physical=hit.Count>0 && hit["collider"].AsGodotObject()==wall;
                        if(physical != (height<7)) throw new InvalidOperationException($"Perimeter ray mismatch {i}/{t}/{height}: {hit}");
                        rays++;
                    }
                }
            }
            Check(true,$"{rays} native rays: continuous below-rim concrete and curved fence; clear space above top.");
            foreach(int index in new[]{0,114,229,343,458,572,687,801})
            {
                Vector3 rim=Vec(sections[index][1]);
                Vector3 outward=(rim-Vec(sections[index][0])) with { Y=0 }; outward=outward.Normalized();
                foreach(bool network in new[]{false,true})
                {
                    foreach(float height in new[]{1f,3.5f})
                    {
                        await Start(network,rim-outward*8+Vector3.Up*height,outward*40+Vector3.Up*(height>3?5:0),Basis.LookingAt(outward));
                        await Frames(90);
                        float distance=_states.Max(s=>(VehicleBody.ToGodot(s.ObservedPhysics.Position)-rim).Dot(outward));
                        Check(distance<1.2f && !_states.Any(s=>s.OutOfBounds),$"{network}/{index}: 40 m/s impact at {height}m contained; maximum outward center {distance:F3}m.");
                        if(index==0 && height>3) await Capture($"{network}-fence-impact",rim-outward*16+Vector3.Up*9,rim+Vector3.Up*3);
                        await Stop();
                    }
                    // Starts inside, below fence top, and clears by actual upward/outward native motion.
                    await Start(network,rim-outward*18+Vector3.Up*3,outward*32+Vector3.Up*28,Basis.LookingAt(outward));
                    await Frames(75);
                    Check(_states.Any(s=>s.OutOfBounds),$"{network}/{index}: ballistic 32m/s outward + 28m/s upward launch cleared finite fence.");
                    if(index==0)
                    {
                        await Capture($"{network}-escape",rim-outward*28+Vector3.Up*22,rim+outward*10+Vector3.Up*9);
                        var exposed=_world.GetVehicle(1);
                        await Frames(60);
                        var measured=_world.GetVehicle(1);
                        ulong ticks=measured.Movement.Tick-exposed.Movement.Tick;
                        float loss=exposed.Damage.CurrentHP-measured.Damage.CurrentHP;
                        Check(Math.Abs(loss-100f*ticks/60)<.02f,$"{network}: measured {loss:F3} HP loss over {ticks} native ticks (100 HP/s).");
                        Check(_hud.Displayed?.OutOfBounds==true,$"{network}: HUD reads confirmed OOB state.");
                        await Frames(540);
                        Check(_states.Any(s=>s.OutOfBounds && s.ObservedPhysics.Position.Y < -30),$"{network}: below-map exterior remains OOB.");
                        Check(_states.Any(s=>s.Lifecycle==VehicleLifecycle.Dead && s.Damage.LastDamage?.Attribution.Source=="out-of-bounds"),$"{network}: 100 HP/s OOB causes ordinary death.");
                        _hud.Refresh();
                        await Frames(180);
                        var current=_world.GetVehicle(1);
                        Check(current.LifeId==2 && current.CanInteract && !current.OutOfBounds && current.Damage.CurrentHP==1000,$"{network}: clean full-health respawn and warning cleared.");
                    }
                    await Stop();
                }
            }
            await Capture("perimeter-overview",new(150,36,120),new(135,5,86));
            GD.Print("Boundary integration passed."); GetTree().Quit();
        }
        catch(Exception error) { _advance=false; Log(error.ToString()); GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private async Task Start(bool network,Vector3 position,Vector3 velocity,Basis basis)
    {
        _world=new(new(60),new RespawnConfiguration(),ActiveMap.ReadConfiguration(_map));
        var rotation=basis.GetRotationQuaternion();
        var pose=new VehiclePhysicsState(VehicleBody.ToCore(position),new N.Quaternion(rotation.X,rotation.Y,rotation.Z,rotation.W),VehicleBody.ToCore(velocity),N.Vector3.Zero);
        var damage=new DamageConfiguration { MaxHP=1000,CollisionScale=5 };
        _world.AddVehicle(1,new(),damage,pose);
        if(network) { _network=new NetworkVehicleBody { VehicleId=1 }; AddChild(_network); _network.Apply(pose); }
        else { _native=new VehicleBody { Transform=new(basis,position),LinearVelocity=velocity,CollisionLayer=2,CollisionMask=1,DamageConfiguration=damage }; _native.Initialize(_world); AddChild(_native); }
        await Frames(2); _states.Clear(); _advance=true;
    }
    private async Task Stop() { _advance=false; _native?.QueueFree(); _network?.QueueFree(); _native=null; _network=null; await Frames(3); }
    private async Task Frames(int count) { for(int i=0;i<count;i++) await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame); }
    private async Task Capture(string name,Vector3 position,Vector3 target)
    {
        if(_camera is null) return;
        _camera.Position=position; _camera.LookAt(target);
        await Frames(3);
        await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        using var image=GetViewport().GetTexture().GetImage();
        Check(image.SavePng(System.IO.Path.Combine(_output,name+".png"))==Error.Ok,"Rendered "+name);
    }
    private void Check(bool condition,string message) { if(!condition) throw new InvalidOperationException(message); Log(message); }
    private void Log(string message) { _lines.Add(message); System.IO.File.WriteAllLines(System.IO.Path.Combine(_output,"evidence.txt"),_lines); GD.Print(message); }
}
