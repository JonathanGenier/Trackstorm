using Godot;
using Trackstorm.Client.Hud;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;
using Trackstorm.Core.Settings;
using Trackstorm.Core.Vehicles;
using Numerics = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Rendered production arena and native HUD checks across settings, source states and resolutions.</summary>
public sealed partial class HudIntegrationChecks : Node
{
    /// <inheritdoc/>
    public override void _Ready() => CallDeferred(MethodName.Run);

    /// <summary>Checks native labels/materials and exports comparable viewport captures.</summary>
    public async void Run()
    {
        try
        {
            string output = OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith("--hud-output=", StringComparison.Ordinal))?[13..] ?? ProjectSettings.GlobalizePath("res://.godot/hud-checks");
            System.IO.Directory.CreateDirectory(output);
            var viewport = new SubViewport { Size = new Vector2I(1280, 720), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            AddChild(viewport);
            var arena = new VehicleArena();
            viewport.AddChild(arena);
            VehicleSnapshot state = arena.Player.Snapshot;
            Require(state.Damage.MaxHP == 1000, "Production practice capacity");
            ItemSlot? slot = null;
            var input = new Input.PlayerInput();
            AddChild(input);
            input.SetPhysicsProcess(false);
            var settings = new Settings.PlayerSettingsController();
            settings.Initialize(input.Adapter, System.IO.Path.Combine(output, "settings.json"));
            AddChild(settings);
            var hud = new CombatHud { Vehicle = () => state, Slot = () => slot, Units = () => settings.Current.SpeedUnit };
            viewport.AddChild(hud);
            var preferences = new Settings.SettingsPanel();
            preferences.Initialize(settings, input.Adapter);
            viewport.AddChild(preferences);
            settings.UpdateSettings(settings.Current with { ShowFps = true, ShowPing = true });
            preferences.SetConnectionTelemetry(new(Networking.ConnectionDiagnosticState.Reconnecting, default));
            var pixels = new List<(int Health, int Speed)>();
            foreach (var sample in new[] { (1000f, 200 / 3.6f, HeldItem.None), (500f, 100 / 3.6f, HeldItem.Wrench), (0f, 0f, HeldItem.None), (850f, 200 / 3.6f, HeldItem.Missile), (850f, 200 / 3.6f, HeldItem.Oil), (850f, 200 / 3.6f, HeldItem.Nitro) })
            {
                state = Sample(state, sample.Item1, sample.Item2);
                slot = new ItemSlot(state.VehicleId, state.LifeId, 1, sample.Item3);
                hud.Refresh();
                Require(hud.HealthText == $"{sample.Item1:0}/1000", "Native health label");
                Require(Math.Abs(hud.HealthFill - (sample.Item1 / 1000.0)) < 0.00001, "Native health material");
                Require(Math.Abs(hud.SpeedFill - CombatHudView.NormalizeSpeed(sample.Item2)) < 0.00001, "Native speed material");
                Require(hud.Displayed!.Item == sample.Item3, "Native item mapping");
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using Image frame = viewport.GetTexture().GetImage();
                pixels.Add((RedPixels(frame, new Rect2I(131, 619, 275, 14)), RedPixels(frame, new Rect2I(814, 585, 211, 99))));
                Require(frame.SavePng(System.IO.Path.Combine(output, $"state-{sample.Item1:0}-{sample.Item3}.png")) == Error.Ok, "State screenshot");
            }

            Require(pixels[0].Health > pixels[1].Health && pixels[1].Health > pixels[2].Health && pixels[2].Health < pixels[0].Health / 10, "Rendered health fill decreases to empty");
            Require(pixels[0].Speed > pixels[1].Speed && pixels[1].Speed > pixels[2].Speed && pixels[2].Speed < pixels[0].Speed / 5, "Rendered speed arc decreases to empty");

            foreach (double charge in new[] { 100.0, 37.5, 0.1, 0.0 })
            {
                slot = new ItemSlot(state.VehicleId, state.LifeId, 1, charge == 0 ? HeldItem.None : HeldItem.Nitro)
                { NitroCharge = charge, SecondToken = 2, SecondItem = HeldItem.Nitro, SecondNitroCharge = 65, ActiveSlot = 1 };
                hud.Refresh();
                Require(hud.Displayed!.ItemName == (charge == 0 ? "EMPTY" : $"NITRO {Math.Ceiling(charge):0}%"), "Authoritative charge label and exhaustion");
                Require(hud.Displayed.SecondItemName == "NITRO 65%", "Independent second-slot charge");
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using Image frame = viewport.GetTexture().GetImage();
                Require(frame.SavePng(System.IO.Path.Combine(output, $"nitro-{charge:0.0}.png")) == Error.Ok, "Charge screenshot");
            }
            slot = slot! with { Item = HeldItem.Nitro, NitroCharge = 37.5, SecondItem = HeldItem.Missile, SecondNitroCharge = 0 };
            VehicleSnapshot before = state;
            slot = slot! with { SecondToken = 2, SecondItem = HeldItem.Missile, ActiveSlot = 1, SelectionRevision = 1 };
            hud.Refresh();
            Require(hud.Displayed!.SecondItem == HeldItem.Missile && hud.Displayed.ActiveSlot == 1, "Both slots and selected second slot render simultaneously");
            settings.UpdateSettings(settings.Current with { SpeedUnit = SpeedUnit.MilesPerHour });
            hud.Refresh();
            Require(hud.Displayed!.Speed == "124" && hud.Displayed.Unit == "mph", "Preferred units");
            Require(ReferenceEquals(before, state), "Unit change is presentation only");
            foreach (Vector2I size in new[] { new Vector2I(640, 360), new Vector2I(960, 540), new Vector2I(1280, 720), new Vector2I(1600, 900), new Vector2I(1920, 1080), new Vector2I(2560, 1440), new Vector2I(3840, 2160), new Vector2I(1024, 768), new Vector2I(2560, 1080) })
            {
                viewport.Size = size;
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                Require(hud.Displayed!.Standing == "--" && hud.Displayed.Timer == "--:--", "Placeholders");
                Require(preferences.DiagnosticsBounds.Position.X >= size.X / 2.0f && preferences.DiagnosticsBounds.End.X <= size.X, "Diagnostics fit the upper-right region");
                using Image frame = viewport.GetTexture().GetImage();
                Require(frame.SavePng(System.IO.Path.Combine(output, $"hud-{size.X}x{size.Y}.png")) == Error.Ok, "Resolution screenshot");
            }

            hud.Vehicle = () => null;
            hud.Refresh();
            Require(!hud.Visible && hud.Displayed is null, "No stale HUD outside match");
            viewport.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            settings.QueueFree();
            input.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree().CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);
            GD.Print("HUD integration passed: state changes, units, 1000 HP, placeholders, nine resolutions, teardown visibility.");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError(exception.ToString());
            GetTree().Quit(1);
        }
    }

    private static VehicleSnapshot Sample(VehicleSnapshot previous, float hp, float speed)
    {
        VehiclePhysicsState pose = new(previous.ObservedPhysics.Position, previous.ObservedPhysics.Orientation, new Numerics.Vector3(speed, 0, 0), Numerics.Vector3.Zero);
        return new VehicleSnapshot(previous.VehicleId, previous.LifeId, new VehicleState(0, pose, false, false, 0, 0), new VehicleDamageState(1000, hp, null, null), pose);
    }

    private static int RedPixels(Image frame, Rect2I area)
    {
        int count = 0;
        for (int y = area.Position.Y; y < area.End.Y; y++)
        {
            for (int x = area.Position.X; x < area.End.X; x++)
            {
                Color pixel = frame.GetPixel(x, y);
                if (pixel.R > 0.25f && pixel.R > pixel.G * 2 && pixel.R > pixel.B * 2)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static void Require(bool value, string label)
    {
        if (!value)
        {
            throw new InvalidOperationException(label);
        }
    }
}
