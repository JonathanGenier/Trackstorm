using Godot;
using Trackstorm.Client.Input;
using Trackstorm.Client.Items;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Matches;

namespace Trackstorm.Client.Verification;

public sealed partial class DeveloperOptionsIntegrationChecks
{
    private PlayerInput? _readabilityPlayer;
    private bool _readabilityRunning;
    private bool _readabilityFiring;
    private ushort _readabilityThrottle;
    private short _readabilitySteering;

    private void AdvanceReadability()
    {
        if (!_readabilityRunning) { return; }
        // The fixture owns synthetic focus/input; no live desktop input enters the samples.
        if (_readabilityPlayer is { } player && !player.Adapter.Enabled)
        {
            player.Adapter.Enabled = true;
            ReadabilityInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
        }
        _host.Advance(new(0, _readabilitySteering, _readabilityThrottle, 0,
            _readabilityFiring ? InputButtons.UseItem : 0, 0, 0));
    }

    private void ReadabilityInput(InputEvent input)
    {
        using (input)
        {
            Godot.Input.ParseInputEvent(input); Godot.Input.FlushBufferedEvents();
            _readabilityPlayer!._Input(input);
        }
    }

    private async Task CheckMachineGunReadability()
    {
        var panel = _devTools.Configs;
        panel.Session = () => _host; await Frames(5);
        var search = Descendants(panel).OfType<LineEdit>().Single(e => e.Name == "ConfigSearch");
        search.Text = "Machine Gun"; search.EmitSignal(LineEdit.SignalName.TextChanged, search.Text);
        Set("items.machine_gun_fire_rate", 4); Press("Apply Settings");
        await Until(() => !panel.AwaitingConfirmation && _host.DeveloperConfiguration.Items.MachineGunFireRate == 4 &&
            _client!.DeveloperConfiguration.Items.MachineGunFireRate == 4, "Dev Options commits reduced four-round aggregate cadence on both peers");
        await Capture("readability-rate-4");
        _client!.Lobby!.Request(Core.Sessions.LobbyCommand.Ready, true);
        _host.Lobby!.Request(Core.Sessions.LobbyCommand.Ready, true);
        await Until(() => _host.Lobby.State!.CanStart, "Both real participants are ready");
        Press("Force Start");
        await Until(() => _host.Arena?.Driver.Match?.Phase == MatchPhase.Active && _client!.Arena?.Driver.Match?.Phase == MatchPhase.Active,
            "Readability uses a real two-peer production-map match");
        Press("Close"); await Frames(8);
        _readabilityPlayer = _bootstrap.GetNode<PlayerInput>("PlayerInput");
        _readabilityPlayer.SetPhysicsProcess(false); _readabilityPlayer.SetProcessInput(false);
        _readabilityPlayer.Adapter.Enabled = true;
        _readabilityRunning = true;
        var arena = _host.Arena!;
        var authority = arena.Driver.Host!;
        ulong shooter = arena.Driver.LocalVehicleId;
        authority.Items.RemovePlayer(shooter);
        Check(authority.Items.Grant(authority.World, shooter, HeldItem.MachineGun), "Fixture grants one actual authoritative Machine Gun for isolated visibility sampling");
        ReadabilityInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
        ReadabilityInput(new InputEventMouseMotion { ScreenRelative = new(0, -70) });
        await Until(() => authority.Items.Aims.Any(a => a.Vehicle == shooter && a.Ready), "Production camera supplies firing-ready shared aim");
        await Frames(90);
        var fire = arena.GetChildren().OfType<ItemPresentation>().Single().MachineGunFire;
        var camera = GetViewport().GetCamera3D()!;
        GD.Print($"READABILITY camera: fov={camera.Fov:F2}, camera-to-car distance={camera.GlobalPosition.DistanceTo(arena.Bodies[shooter].VisualPosition):F2}m, viewport={GetViewport().GetVisibleRect().Size}");
        var records = new List<string> { "round,gun,gun_round,expected_color,matching_rendered_pixels" };
        var sampled = new HashSet<int>();
        var remote = new List<ItemEvent>();
        void RecordRemote(ItemPublication state) => remote.AddRange(state.Events.Where(e => e.Owner == shooter && e.Item == HeldItem.MachineGun));
        _client!.Arena!.Driver.ItemsReceived += RecordRemote;
        _readabilityFiring = true; Check(arena.Driver.RequestItemUse(), "Start reduced-rate held fire");
        for (int frame = 0; frame < 1200 && sampled.Count < 18; frame++)
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            for (int i = 0; i < fire.Multimesh.VisibleInstanceCount; i++)
            {
                if (fire.NewlyPresentedRound(i) is not { } shot || shot.Owner != shooter || shot.MachineGunRound >= 18 || !sampled.Add(shot.MachineGunRound)) { continue; }
                int side = shot.MachineGunBarrel;
                string color = shot.MachineGunRound % 6 < 4 ? "gold" : side == 0 ? "red" : "green";
                using var pixels = GetViewport().GetTexture().GetImage();
                int visible = CountRoundPixels(pixels, camera, fire.Multimesh.GetInstanceTransform(i), color);
                string gun = side == 0 ? "left" : "right";
                records.Add($"{shot.MachineGunRound},{gun},{shot.MachineGunRound / 2 + 1},{color},{visible}");
                string path = System.IO.Path.Combine(_directory, $"sequence-{shot.MachineGunRound:D2}-{gun}-{color}.png");
                Check(pixels.SavePng(path) == Error.Ok, "Captured actual rendered sequence round");
                GD.Print($"READABILITY round {shot.MachineGunRound}: {gun} {color}, {visible} matching luminous pixels");
                Check(visible >= 6, $"Round {shot.MachineGunRound} {gun} {color} must be visibly present in the rendered pixels, found {visible}");
            }
        }
        _readabilityFiring = false; await Frames(45);
        Check(sampled.Count == 18 && remote.Count >= 18, "Three visually sampled gold/gold/colored cycles per gun reach the real observer");
        System.IO.File.WriteAllLines(System.IO.Path.Combine(_directory, "sequence-pixels.csv"), records);
        Tap(Key.F1); await Frames(8);
        Set("items.machine_gun_fire_rate", 80); Press("Apply Settings");
        await Until(() => !panel.AwaitingConfirmation && _host.DeveloperConfiguration.Items.MachineGunFireRate == 80 &&
            _client.DeveloperConfiguration.Items.MachineGunFireRate == 80, "Dev Options restores production cadence on both peers");
        Check(_host.DeveloperConfiguration.Items == Core.Development.GameplayConfiguration.HostedDefaults.Items && MachineGunVfxSettings.Current == new MachineGunVfxSettings(),
            "Production gameplay and local visual defaults restored before sustained fire");
        await Capture("readability-restored-defaults");
        Press("Close"); await Frames(8);
        ReadabilityInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
        await Frames(30);
        int before = fire.EmittedCount;
        _readabilityFiring = true; arena.Driver.RequestItemUse();
        float peakSpeed = 0;
        for (int frame = 0; frame < 300; frame++)
        {
            _readabilityThrottle = ushort.MaxValue; _readabilitySteering = (short)(frame < 150 ? 5000 : -5000);
            if (frame % 45 == 0) { ReadabilityInput(new InputEventMouseMotion { ScreenRelative = new(frame % 90 == 0 ? 50 : -50, 0) }); }
            await Frames(1);
            peakSpeed = Math.Max(peakSpeed, authority.World.GetVehicle(shooter).Speed);
            if (frame % 60 == 30) { await Capture($"readability-default-moving-{frame:D3}"); }
        }
        _readabilityFiring = false; _readabilityThrottle = 0; _readabilitySteering = 0;
        await Frames(45);
        Check(fire.EmittedCount - before > 200 && peakSpeed > 5, $"Production sustained moving fire: {fire.EmittedCount - before} presented rounds, peak speed {peakSpeed:F2}m/s");
        Check(fire.ActiveCount == 0, "Visible fire drains after release");
        GD.Print($"READABILITY default sustained: {fire.EmittedCount - before} presented rounds, peak speed {peakSpeed:F2}m/s; all defaults restored.");
        _client.Arena.Driver.ItemsReceived -= RecordRemote;
        _readabilityRunning = false;
    }

    // Pixel observation is independent of the shader custom data and authority's tracer flag.
    // Inspect the interior of the projected moving quad, avoiding its muzzle flash and tapered ends.
    private static int CountRoundPixels(Image image, Camera3D camera, Transform3D quad, string color)
    {
        var points = new List<Vector2>();
        foreach (float x in new[] { -.5f, .5f })
        foreach (float y in new[] { -.3f, .3f })
        { points.Add(camera.UnprojectPosition(quad * new Vector3(x, y, 0))); }
        int left = Math.Max(0, (int)MathF.Floor(points.Min(p => p.X)) - 1);
        int right = Math.Min(image.GetWidth() - 1, (int)MathF.Ceiling(points.Max(p => p.X)) + 1);
        int top = Math.Max(0, (int)MathF.Floor(points.Min(p => p.Y)) - 1);
        int bottom = Math.Min(image.GetHeight() - 1, (int)MathF.Ceiling(points.Max(p => p.Y)) + 1);
        int count = 0;
        for (int y = top; y <= bottom; y++)
        for (int x = left; x <= right; x++)
        {
            var p = image.GetPixel(x, y);
            bool match = color == "gold" ? p.R > .7f && p.G > .55f && p.B < .65f
                : color == "red" ? p.R > .7f && p.G < .5f && p.B < .4f
                : p.G > .7f && p.R < .5f && p.B < .6f;
            if (match) { count++; }
        }
        return count;
    }
}
