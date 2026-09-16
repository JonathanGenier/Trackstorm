using Godot;
using Trackstorm.Client.Audio;
using Trackstorm.Client.Input;
using Trackstorm.Client.Settings;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Settings;

namespace Trackstorm.Client.Verification;

/// <summary>Native stream, bus, playlist-completion and arena ownership verification.</summary>
public sealed partial class AudioIntegrationChecks : Node
{
    private int _assertions;

    /// <inheritdoc/>
    public override void _Ready() => CallDeferred(MethodName.Run);

    /// <summary>Uses real imported streams and native completion signals, then exits.</summary>
    public async void Run()
    {
        try
        {
            var input = new PlayerInput();
            AddChild(input);
            input.SetPhysicsProcess(false);
            var settings = new PlayerSettingsController();
            settings.Initialize(input.Adapter, ProjectSettings.GlobalizePath("res://.godot/audio-check-settings.json"));
            AddChild(settings);
            foreach (AudioCue cue in Enum.GetValues<AudioCue>())
            {
                AudioStream stream = GD.Load<AudioStream>(AudioCatalog.Path(cue));
                Require(stream.GetLength() > 0.01, $"Imported playable stream: {cue}");
            }

            for (int index = 0; index < 3; index++)
            {
                var song = GD.Load<AudioStreamMP3>(AudioCatalog.Song(index));
                Require(song.GetLength() > 170 && !song.Loop, $"Song {index + 1} is complete and does not loop independently.");
            }

            settings.UpdateSettings(new PlayerSettings { MasterVolume = 0.5, MusicVolume = 0.25, SfxVolume = 0 });
            Require(Math.Abs(AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex("Master")) + 6.0206) < 0.01, "Existing Master settings apply.");
            Require(Math.Abs(AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex("Music")) + 12.0412) < 0.01, "Music gain is independent.");
            Require(AudioServer.IsBusMute(AudioServer.GetBusIndex("SFX")), "SFX mute applies to its descendants.");
            foreach (string child in new[] { "Vehicle", "Weapons", "UI" })
            {
                Require(AudioServer.GetBusSend(AudioServer.GetBusIndex(child)) == "SFX", $"{child} feeds SFX.");
            }

            foreach (string child in new[] { "Music", "SFX" })
            {
                Require(AudioServer.GetBusSend(AudioServer.GetBusIndex(child)) == "Master", $"{child} feeds Master.");
            }

            settings.UpdateSettings(new PlayerSettings());
            var arena = new ArenaAudio();
            AddChild(arena);
            Require(arena.MusicPlaying && arena.TrackIndex is >= 0 and < 3, "Arena entry immediately starts one valid track without match state or players.");
            AudioStreamPlayer player = arena.GetChildren().OfType<AudioStreamPlayer>().Single(node => node.Bus == "Music");
            player.Seek(30);
            await ToSignal(GetTree().CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);
            int initial = arena.TrackIndex;
            AudioStream initialStream = player.Stream;
            ulong revision = 0;
            foreach (MatchPhase phase in new[] { MatchPhase.Waiting, MatchPhase.Countdown, MatchPhase.Active, MatchPhase.Finished })
            {
                PlayerScore[] scores = phase == MatchPhase.Finished ? [new(1, 5, 0, 1, 0), new(2, 0, 5, 0, 5)] : [];
                var state = new MatchState(++revision, revision, 5, phase, phase == MatchPhase.Countdown ? 181ul : null, phase == MatchPhase.Finished ? 1ul : null, scores);
                arena.ApplyMatch(state);
                arena.ApplyMatch(state);
                arena.ApplyMatch(state, true);
                Require(arena.MusicPlaying && arena.TrackIndex == initial && player.Stream == initialStream && player.GetPlaybackPosition() >= 29, $"{phase}, duplicates and reconnect seeding preserve the current stream, position and playlist.");
            }

            Require(arena.CueCount >= 3, "Match start/end/sting cues remain available independently of music.");
            for (int step = 0; step < 4; step++)
            {
                int index = arena.TrackIndex;
                player.Seek((float)player.Stream.GetLength() - 0.12f);
                for (int wait = 0; wait < 100 && arena.TrackIndex == index; wait++)
                {
                    await ToSignal(GetTree().CreateTimer(0.02), SceneTreeTimer.SignalName.Timeout);
                }

                Require(arena.TrackIndex == (index + 1) % 3, "Real MP3 completion advances sequentially, including wraparound.");
            }

            RemoveChild(arena);
            Require(!arena.MusicPlaying && arena.TrackIndex == -1 && player.Stream is null, "Arena exit stops, resets and releases the stream.");
            arena.NextSong();
            Require(arena.TrackIndex == -1, "Late completion cannot restart an exited arena.");
            arena.Free();
            Require(!IsInstanceValid(arena) && !IsInstanceValid(player), "Arena exit frees music and every owned voice.");
            var reentered = new ArenaAudio();
            AddChild(reentered);
            Require(reentered.MusicPlaying && reentered.TrackIndex is >= 0 and < 3, "Re-entering a new arena starts a fresh playlist.");
            RemoveChild(reentered);
            reentered.Free();
            await CheckLonePlayer();
            await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
            GD.Print($"Audio integration passed: {_assertions} assertions.");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError(exception.ToString());
            GetTree().Quit(1);
        }
    }

    private async Task CheckLonePlayer()
    {
        using var reservation = new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
        string address = $"127.0.0.1:{((System.Net.IPEndPoint)reservation.Client.LocalEndPoint!).Port}";
        reservation.Close();
        using var hostGateway = new Networking.GameNetworkingSocketsTransport();
        using var clientGateway = new Networking.GameNetworkingSocketsTransport();
        hostGateway.Listen(Core.Networking.Transport.TransportEndpoint.DirectIp(address));
        var hostView = new SubViewport { OwnWorld3D = true };
        var clientView = new SubViewport { OwnWorld3D = true };
        AddChild(hostView);
        AddChild(clientView);
        var host = new Networking.NetworkVehicleArena();
        host.Initialize(hostGateway, 340, 0);
        hostView.AddChild(host);
        Require(host.Audio.MusicPlaying, "Production arena starts playback immediately with only its host.");
        for (int frame = 0; frame < 30; frame++)
        {
            host.Advance(default);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }

        Require(host.Driver.Latest?.Vehicles.Count == 1 && host.Driver.Match?.Phase == MatchPhase.Waiting && host.Audio.MusicPlaying, "Lone player has native music while authoritative match is Waiting for players.");
        var player = host.Audio.GetChildren().OfType<AudioStreamPlayer>().Single(node => node.Bus == "Music");
        int index = host.Audio.TrackIndex;
        player.Seek(30);
        var client = new Networking.NetworkVehicleArena();
        client.Initialize(clientGateway, 0, clientGateway.Connect(Core.Networking.Transport.TransportEndpoint.DirectIp(address)));
        clientView.AddChild(client);
        bool countdown = false;
        for (int frame = 0; frame < 600; frame++)
        {
            host.Advance(default);
            client.Advance(default);
            countdown |= host.Driver.Match?.Phase == MatchPhase.Countdown;
            if (host.Driver.Match?.Phase == MatchPhase.Active && client.Driver.Match?.Phase == MatchPhase.Active)
            {
                break;
            }

            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }

        Require(countdown && host.Driver.Match?.Phase == MatchPhase.Active && client.Driver.Match?.Phase == MatchPhase.Active, "Second real UDP peer joins and both reach Active through countdown.");
        Require(host.Audio.MusicPlaying && host.Audio.TrackIndex == index && player.GetPlaybackPosition() >= 29 && client.Audio.MusicPlaying, "Joining peer, countdown and activation preserve the host song and playback position.");
        hostView.QueueFree();
        clientView.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Require(!IsInstanceValid(player), "Leaving the production arena frees its music player.");
    }

    private void Require(bool condition, string description)
    {
        if (!condition)
        {
            throw new InvalidOperationException(description);
        }

        _assertions++;
    }
}
