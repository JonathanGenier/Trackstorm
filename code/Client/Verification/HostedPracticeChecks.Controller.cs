using Godot;
using Trackstorm.Client.Input;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

public sealed partial class HostedPracticeChecks
{
    private async Task CheckControllerEmp(DevelopmentSession session, DevelopmentSession remote, ulong[] cars)
    {
        var arena = session.Arena!;
        var host = arena.Driver.Host!;
        ulong owner = arena.Driver.LocalVehicleId;
        var input = new PlayerInput(); AddChild(input);
        input.SetPhysicsProcess(false); input.SetProcessInput(false);
        input.GameplayAvailable = () => true;
        _aimInput = input;
        arena.CameraInput = input.Adapter;
        var camera = arena.GetNode<VehicleChaseCamera>("ChaseCamera");
        var view = (SubViewport)session.GetParent();
        view.Size = new(1280, 800); view.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        string output = ProjectSettings.GlobalizePath("res://.godot/hosted-controller-emp");
        System.IO.Directory.CreateDirectory(output);
        Vector2 held = default;
        void Stick(Vector2 value)
        {
            held = value;
            input.Adapter.Enabled = true;
            foreach (var (axis, amount) in new[] { (JoyAxis.RightX, value.X), (JoyAxis.RightY, value.Y) })
            {
                using var motion = new InputEventJoypadMotion { Device = 0, Axis = axis, AxisValue = amount };
                Godot.Input.ParseInputEvent(motion); Godot.Input.FlushBufferedEvents(); input._Input(motion);
            }
        }
        _restoreControllerInput = () => { var restore = held; Stick(new(.16f, 0)); Stick(restore); };
        var events = new List<ItemEvent>();
        var captures = new List<(string Name, Image Image)>();
        arena.AimReleaseObserved = reason => GD.Print("CONTROLLER_RELEASE " + reason);
        void Observe(ItemPublication state) => events.AddRange(state.Events.Where(e => e.Owner == owner && e.Item == HeldItem.EmpClaw));
        arena.Driver.ItemsReceived += Observe;
        try
        {
            // Only place the shooter. All three targets keep their actual TS-303
            // identities, route input, suspension and native movement.
            using var ground = PhysicsRayQueryParameters3D.Create(new(-25, 80, 0), new(-25, -10, 0), 1);
            using var support = arena.GetWorld3D().DirectSpaceState.IntersectRay(ground);
            Require(support.Count > 0, "tabletop support exists");
            Vector3 position = support["position"].AsVector3() + Vector3.Up * 1.2f;
            // Face +Z so the bank's downward shots clear the rear of the car;
            // EMP's unchanged enlarged self-clearance still governs readiness.
            var pose = new VehiclePhysicsState(VehicleBody.ToCore(position), N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitY, MathF.PI), N.Vector3.Zero, N.Vector3.Zero);
            var state = host.World.State;
            host.World.Restore(new(state.Tick, state.LastInput, state.Vehicles.Select(v => v.VehicleId != owner ? v :
                new VehicleSnapshot(v.VehicleId, v.LifeId, new(state.Tick, pose, false, false, 0, 0), v.Damage, pose)), state.Match));
            arena.Bodies[owner].Apply(pose);
            await Until(() => host.World.GetVehicle(owner).Movement.Grounded, "shooter settled on tabletop");
            camera.ResetFollow();
            GD.Print($"CONTROLLER_TABLETOP position={position}; synthetic stick capped at .65; no mouse events or direct camera aim writes");
            for (int sequence = 0; sequence < 2; sequence++)
            {
                bool testedRelease = false;
                host.Items.RemovePlayer(owner);
                await Frames(100);
                Require(arena.Driver.GiveDeveloperItem(HeldItem.EmpClaw), "ordinary developer grant provides three-shot EMP");
                // Deployment is distinct from firing readiness: the default EMP
                // ray may be self-blocked until the controller aims at a rival.
                await Until(() => arena.Driver.LocalItem is { } slot && slot.Active.Item == HeldItem.EmpClaw &&
                    host.Items.EmpClawReady(slot), "actual EMP deployment");
                for (int index = 0; index < 3; index++)
                {
                    ulong target = cars[index];
                    int frames = 0;
                    int retainedFrames = 0;
                    float maximumStick = 0, initialError = 0;
                    for (; frames < 900 && retainedFrames < 20; frames++)
                    {
                        if (arena.AssistedCar == target)
                        {
                            Stick(Vector2.Zero);
                            await Frames(1);
                            retainedFrames = arena.AssistedCar == target ? retainedFrames + 1 : 0;
                            continue;
                        }
                        retainedFrames = 0;
                        Vector3 local = camera.GlobalBasis.Inverse() * (arena.Bodies[target].VisualTransform * new Vector3(0, .3f, 0) - camera.GlobalPosition);
                        Vector3 cursor = camera.GlobalBasis.Inverse() * camera.ProjectRayNormal(arena.AimOverlay.Marker ?? view.GetVisibleRect().GetCenter());
                        Vector2 error = new(Mathf.AngleDifference(MathF.Atan2(cursor.X, -cursor.Z), MathF.Atan2(local.X, -local.Z)),
                            -MathF.Atan2(local.Y, new Vector2(local.X, local.Z).Length()) + MathF.Atan2(cursor.Y, new Vector2(cursor.X, cursor.Z).Length()));
                        if (frames == 0) { initialError = error.Length(); }
                        // A simple visual feedback approximation to a moderate stick sweep.
                        // It does not set the target identity or bypass acquisition/retention.
                        float amount = Math.Clamp(.15f + MathF.Sqrt(error.Length() / 1.2f) * .85f, .22f, .65f);
                        Vector2 command = error.Normalized() * amount;
                        maximumStick = Math.Max(maximumStick, command.Length());
                        Stick(command);
                        await Frames(1);
                        Require(arena.AimOverlay.Marker.HasValue, "square persists while controller searches/switches");
                    }
                    Stick(Vector2.Zero);
                    GD.Print($"CONTROLLER_EMP sequence={sequence} car={index + 1} id={target} frames={frames} initialError={initialError:F4} peakStick={maximumStick:F3} range={position.DistanceTo(arena.Bodies[target].VisualPosition):F2} selected={arena.AssistedCar} {arena.AimTargetDiagnostics(target)}");
                    Require(arena.AssistedCar == target, "controller acquires requested practice car");
                    Require(index == 0 || frames > 0, "switching to the next car requires directional controller input");
                    Require(retainedFrames == 20 && arena.AssistedCar == target && arena.AimOverlay.Marker.HasValue && arena.TargetIndicator.Target == target,
                        "neutral controller retains requested car with persistent square and separate feedback");
                    if (index == 2 && !testedRelease)
                    {
                        Stick(new(.65f, 0)); await Frames(45);
                        Stick(Vector2.Zero); await Frames(3);
                        Require(arena.AssistedCar != target && arena.AimOverlay.Marker.HasValue,
                            "deliberate controller sweep releases original target and keeps the square");
                        testedRelease = true;
                        index--; continue; // Reacquire through input before spending the third round.
                    }
                    if (DisplayServer.GetName() != "headless")
                    {
                        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        captures.Add(($"sequence-{sequence}-car-{index + 1}.png", view.GetTexture().GetImage()));
                    }
                    await Frames(15);
                    await Until(() => arena.Driver.LocalItem is { } slot && arena.Bodies[owner].Rack.IsEmpClawReady(slot) &&
                        host.Items.EmpClawReady(slot) && host.World.State.Tick >= slot.Active.EmpClaw!.ReadyTick, "EMP interval ready");
                    ulong token = arena.Driver.LocalItem!.Active.Token;
                    Basis beforeShot = camera.GlobalBasis;
                    Require(arena.Driver.RequestItemUse(), "ordinary EMP use accepted");
                    await Until(() => events.Any(e => e.Token == token && !e.Impact), "real EMP launch committed");
                    Require(host.Items.Missiles.Single(p => p.Id == token).EmpClaw?.Target == target,
                        "authoritative EMP captured the requested acquired car");
                    int remaining = 2 - index;
                    await Until(() => remote.Arena!.Driver.ItemState?.Slots.Any(s => s.Vehicle == owner &&
                        (remaining == 0 ? s.Active.Item == HeldItem.None : s.Active.EmpClaw?.Remaining == remaining)) == true,
                        "remote confirms one round spent, preserving the three-shot magazine");
                    if (remaining > 0)
                    {
                        float drift = (-beforeShot.Z).AngleTo(-camera.GlobalBasis.Z);
                        GD.Print($"CONTROLLER_SHOT_HOLD sequence={sequence} car={index + 1} drift={drift:F6}");
                        Require(drift < .015f, "neutral controller shot transition does not drag the camera toward chase");
                    }
                }
            }
            // Encoding PNGs can stall the main loop long enough to expire transient
            // aim. Keep it outside the timed acquisition/fire/transition sequence.
            foreach (var capture in captures) { capture.Image.SavePng(System.IO.Path.Combine(output, capture.Name)); }
        }
        finally
        {
            Stick(Vector2.Zero);
            arena.Driver.ItemsReceived -= Observe;
            foreach (var capture in captures) { capture.Image.Dispose(); }
            arena.AimReleaseObserved = null;
            _restoreControllerInput = null;
            _aimInput = null;
            arena.CameraInput = null;
            input.QueueFree();
        }
    }
}
