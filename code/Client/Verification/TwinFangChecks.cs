using Godot;
using Trackstorm.Client.Items;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Verification;

/// <summary>Rendered production-rig poses and confirmed-fire lifecycle checks; network checks run separately.</summary>
public sealed partial class TwinFangChecks : Node3D
{
    private readonly string _output = "res://.godot/ts234-visual";
    public override void _Ready() => CallDeferred(MethodName.Run);

    private async void Run()
    {
        try
        {
            Engine.MaxFps = 60;
            bool motion = OS.GetCmdlineUserArgs().Contains("--twin-fang-motion");
            System.IO.Directory.CreateDirectory(ProjectSettings.GlobalizePath(_output));
            AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new(.16f, .19f, .22f), AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new(.8f, .86f, 1), AmbientLightEnergy = .6f, TonemapMode = Godot.Environment.ToneMapper.Filmic } });
            AddChild(new DirectionalLight3D { RotationDegrees = new(-45, -30, 0), LightEnergy = 2, ShadowEnabled = true });
            var model = VehicleVisual.Create(new StandardMaterial3D { AlbedoColor = new(.5f, .05f, .04f) });
            AddChild(model);
            var deployment = model.GetChildren().OfType<CarDeployment>().Single();
            deployment.SetTimelineProgress(1);
            var rack = model.GetNode<Node3D>("WeaponRack");
            var mount = new WeaponAimMount(HeldItem.MachineGun) { Position = new(0, WeaponAim.Pivot.Y - 1.34f, 0) };
            rack.AddChild(mount);
            var camera = new Camera3D { Fov = 44, Current = true, Position = new(4.5f, 3.5f, 6.5f) };
            AddChild(camera); camera.LookAt(new(0, .9f, .3f));
            var inventory = new ItemSlot(1, 1, 1, HeldItem.MachineGun) { EngagedToken = 1 };
            ulong tick = 1;
            foreach (var pose in new[] { ("forward", 0f, 0f), ("side", MathF.PI / 2, 0f), ("rear", MathF.PI, 0f), ("overhead", 0f, MathF.PI / 2) })
            {
                if (pose.Item1 == "overhead")
                { camera.Position = new(5.2f, 4.1f, 7.5f); camera.LookAt(new(0, 1.15f, .3f)); }
                var aim = new WeaponAimSolution(1, 1, 1, tick, pose.Item2, pose.Item3, WeaponAim.Pivot,
                    WeaponAim.Direction(pose.Item2, pose.Item3), true) { Ready = true };
                for (int i = 0; i < 50; i++) { mount.Present(aim, 1f / 60); await Frame(); }
                if (Math.Abs(mount.TwinFang!.Pitch - pose.Item3) > .001f) { throw new InvalidOperationException("Pitch mismatch"); }
                await Capture(pose.Item1);
            }
            var forward = new WeaponAimSolution(1, 1, 1, tick, 0, 0, WeaponAim.Pivot, -System.Numerics.Vector3.UnitZ, true) { Ready = true };
            camera.Position = new(1.8f, 2.8f, 4); camera.LookAt(new(0, 1.6f, 1.5f));
            float before = mount.TwinFang!.FeedPhase;
            var belt = mount.TwinFang.GetChildren().OfType<MultiMeshInstance3D>().First().Multimesh;
            Transform3D previousLink = Transform3D.Identity;
            for (int i = 0; i < 180; i++)
            {
                mount.TwinFang.Observe(inventory, new[] { new ItemEvent(1, 1, HeldItem.MachineGun, new(0, 2, -20), false) { MachineGunRound = (int)tick } }, ++tick);
                mount.Present(forward, 1f / 60);
                if (i == 80) { previousLink = belt.GetInstanceTransform(4); }
                if (i == 81 && previousLink.Origin.DistanceTo(belt.GetInstanceTransform(4).Origin) < .0001f)
                { throw new InvalidOperationException("Visible cartridges did not move between firing frames"); }
                if (i == 80 || i == 81) { await Capture("firing-" + i); }
                if (motion && i >= 80 && i < 140) { await Capture("motion-" + (i - 80).ToString("D3")); }
                await Frame();
            }
            if (mount.TwinFang.FeedPhase == before) { throw new InvalidOperationException("Belts did not advance"); }
            mount.TwinFang.Observe(inventory with { EngagedToken = 0 }, [], ++tick);
            float stopped = mount.TwinFang.FeedPhase;
            for (int i = 0; i < 30; i++)
            {
                mount.Present(forward, 1f / 60); await Frame();
                if (motion) { await Capture("motion-" + (60 + i).ToString("D3")); }
            }
            if (mount.TwinFang.FlashVisible || mount.TwinFang.FeedPhase != stopped) { throw new InvalidOperationException("Release did not stop effects"); }
            await Capture("released");
            mount.TwinFang.Observe(inventory, new[] { new ItemEvent(1, 1, HeldItem.MachineGun, new(0, 2, -20), false) { MachineGunRound = (int)tick } }, ++tick);
            mount.Present(forward, .1f);
            if (!mount.TwinFang.FlashVisible) { throw new InvalidOperationException("Long render frame erased new flash"); }
            mount.Present(forward, .1f);
            if (mount.TwinFang.FlashVisible) { throw new InvalidOperationException("Flash survived its bounded envelope"); }
            mount.TwinFang.Stop();
            for (int frame = 0; frame < 240; frame++)
            {
                float yaw = frame / 240f * MathF.Tau;
                mount.Present(forward with { Yaw = yaw, Pitch = MathF.Sin(frame / 240f * MathF.PI) * MathF.PI / 2 }, 1f / 60);
                if (!rack.Rotation.IsZeroApprox()) { throw new InvalidOperationException("Rack rotated with payload"); }
                await Frame();
                if (frame % 40 == 0) { await Capture("sweep-" + frame); }
            }
            // Inspect the production material on both drums and all receiver faces,
            // at close range as well as the established full-vehicle views above.
            mount.Present(forward, 1f / 60);
            foreach (var view in new[]
            {
                ("skin-front", new Vector3(2.1f, 2.7f, -3.1f)),
                ("skin-rear", new Vector3(-2.1f, 2.6f, 2.8f)),
                ("skin-left", new Vector3(-2.7f, 2.2f, 0)),
                ("skin-right", new Vector3(2.7f, 2.2f, 0)),
                ("skin-top", new Vector3(.1f, 5f, .1f)),
            })
            {
                camera.Position = view.Item2;
                camera.LookAt(new(0, 1.8f, -.3f));
                await Frame();
                await Capture(view.Item1);
            }
            model.Visible = false;
            var mounts = new List<WeaponAimMount>();
            for (int car = 0; car < 8; car++)
            {
                var other = VehicleVisual.Create(new StandardMaterial3D { AlbedoColor = new(.5f, .05f, .04f) });
                other.Position = new((car % 4 - 1.5f) * 3.6f, 0, (car / 4) * 6);
                AddChild(other); other.GetChildren().OfType<CarDeployment>().Single().SetTimelineProgress(1);
                var weapon = new WeaponAimMount(HeldItem.MachineGun) { Position = mount.Position };
                other.GetNode<Node3D>("WeaponRack").AddChild(weapon); mounts.Add(weapon);
            }
            camera.Position = new(13, 10, 19); camera.LookAt(new(0, 1, 3));
            for (int frame = 0; frame < 600; frame++)
            {
                foreach (var weapon in mounts)
                {
                    weapon.TwinFang!.Observe(inventory, new[] { new ItemEvent(1, 1, HeldItem.MachineGun, new(0, 2, -20), false) { MachineGunRound = (int)tick } }, ++tick);
                    weapon.Present(forward with { Yaw = MathF.Sin(frame / 60f) * .45f }, 1f / 60);
                }
                await Frame();
                if (frame == 300) { await Capture("eight-shooters"); }
            }
            foreach (var weapon in mounts) { weapon.TwinFang!.Stop(); }
            await Capture("eight-stopped");
            GD.Print("Twin Fang visual checks passed: forward, side, rear, overhead, full yaw/elevation sweep, confirmed belt motion, release, low-FPS flash and eight simultaneous scripted shooters.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private async Task Frame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    private async Task Capture(string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using Image image = GetViewport().GetTexture().GetImage();
        image.SavePng(ProjectSettings.GlobalizePath(_output + "/" + name + ".png"));
    }
}
