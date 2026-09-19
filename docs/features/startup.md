# Startup application flow

Fresh production startup follows one explicit sequence:

`Preloader → Splash Screen → MenuShell / Loader → Main Menu`

`SimulationBootstrap` remains the application composition root. The main scene enables its startup controller; isolated integration fixtures construct the same bootstrap without replaying presentation delays. Command-line verification entry points continue to run before the product startup sequence so their existing ownership and timing remain unchanged.

## Ownership and transitions

`StartupFlow` accepts only the required ordered transitions. The Preloader presents one frame, loads only the Splash and two immediate MenuShell media dependencies, and then creates the dedicated `SplashScreen`; it performs no global application initialization. `SplashScreen` owns a full-screen one-shot Ogg Theora video with its synchronized embedded Vorbis audio on the Master bus. Native video completion—not a timer or player input—advances the flow. After completion the Splash owner is removed, `StartupController` creates one `MenuShell`, and the separate Loader video/music begin from their starts. The shell is never replaced while moving from Loader to Main Menu.

`MenuShell` owns the approved silent Ogg Theora background, loader/failure chrome and separately routed authoritative MP3 player. Saved audio settings and the Music bus initialize before playback begins. The video player has zero embedded-audio gain and its runtime OGV contains no audio stream; music remains an independent looping `AudioStreamPlayer` on the settings-controlled Music bus. Each stream loops independently. Entering gameplay hides the shell and stops both players; returning to the frontend resumes the same shell owner.

The existing multiplayer menu and Settings entry are composed hidden while the Loader is visible. Successful initialization fades those existing controls in while fading Loader chrome out. Neither media player is restarted or recreated during this transition; the Main Menu appears over the exact presentation that began with the Loader.

## Application initialization

The Loader retains the reusable resources that existing menu and gameplay presentation already require: common HUD textures/icons, shared HUD and damage shaders, the arena music tracks, ambience, vehicle loops, combat cues and UI cues. Resources load incrementally across frames before the existing application composition creates diagnostics, HUD systems, EOS identity where enabled, and the multiplayer Main Menu/session owner. The minimum settings/audio owner is created at MenuShell entry so saved mute and Music volume apply before the authoritative frontend MP3 starts.

Loading or required-system composition failure enters a blocking failure panel inside the same MenuShell. Main Menu remains absent. Retry clears the partial resource set and reruns the Loader; Quit exits with failure. Invalid state skips are rejected by the pure startup state machine.

## Verification

`StartupFlowTests` covers successful order, failure/retry and invalid transition rejection without Godot. `check-startup.ps1 -GodotPath <Godot .NET executable>` checks all startup media hashes, runs the production main scene, lets the actual Splash finish, injects one required-initialization failure, retries through the production recovery path, and verifies ordered states, active one-shot Splash playback without MenuShell media, Splash teardown before Loader, Main Menu gating, and exact Loader video/music player instance continuity. The [frontend media record](../../assets/frontend/README.md) owns source preservation, conversion and hashes. Visual scaling, audio/video synchronization, loop-seam quality and audible output still require a rendered/manual run.

[Feature index](README.md) · [Game Menu](game-menu.md) · [Settings](settings.md) · [Arena audio](audio.md)
