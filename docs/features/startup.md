# Startup application flow

Fresh production startup follows one explicit sequence:

`Preloader → Splash Screen → MenuShell / Loader → Main Menu`

`SimulationBootstrap` remains the application composition root. The main scene enables its startup controller; isolated integration fixtures construct the same bootstrap without replaying presentation delays. Command-line verification entry points continue to run before the product startup sequence so their existing ownership and timing remain unchanged.

## Ownership and transitions

`StartupFlow` accepts only the required ordered transitions. The Preloader presents one frame, loads only the Splash and two immediate MenuShell media dependencies, and then creates the dedicated `SplashScreen`; it performs no global application initialization. `SplashScreen` owns a full-screen one-shot Ogg Theora video with its synchronized embedded Vorbis audio on the Master bus. Native video completion—not a timer or player input—advances the flow. After completion the Splash owner is removed, `StartupController` creates one `MenuShell`, and the separate Loader video/music begin from their starts. The shell is never replaced while moving from Loader to Main Menu.

`MenuShell` owns the approved silent Ogg Theora background, loader/failure chrome and separately routed authoritative MP3 player. Saved audio settings and the Music bus initialize before playback begins. The video player has zero embedded-audio gain and its runtime OGV contains no audio stream; music remains an independent looping `AudioStreamPlayer` on the settings-controlled Music bus. Each stream loops independently. Entering the admitted Lobby or Match Loader hides the shell and stops both players; returning to the frontend resumes the same shell owner.

The existing multiplayer owner, modular [Main Menu](main-menu.md), and Settings owner are composed hidden while the Loader is visible. Successful initialization starts the Main Menu's heavy drop/catch/settle entrance while fading Loader chrome out. Its native targets remain disabled until the settle completes; browser and Settings retain their existing navigation. Neither media player is restarted or recreated during this transition; the Main Menu appears over the exact presentation that began with the Loader. The Loader preloads the menu's reusable textures and shaders alongside existing shared resources.

[Podium](post-match.md) Main Menu waits for explicit session/membership cleanup before resuming this same MenuShell. Controlled Quit keeps the shell suspended rather than starting its media again as session teardown completes.

## Application initialization

The Loader retains reusable HUD textures/icons, the shared HUD shader and interface cues. Arena music, ambience, vehicle/combat sounds, damage shader and the selected map are loaded separately by the [Match Loader](match-entry.md). Resources load incrementally across frames before the existing application composition creates diagnostics, HUD systems, EOS identity where enabled, and the multiplayer Main Menu/session owner. The minimum settings/audio owner is created at MenuShell entry so saved mute and Music volume apply before the authoritative frontend MP3 starts.

Required failures are owned by one of three explicit recovery phases: frontend dependencies, MenuShell/frontend setup, or application initialization. A dependency failure creates a media-independent failure shell with Retry/Quit; Retry clears partial presentation resources and replays Preloader loading before Splash. A frontend setup failure clears any partial media assignment and retries saved settings plus MenuShell media startup before application loading. An application failure rolls back partially composed systems, restores native auto-quit ownership, and retries only reusable application loading; the already-valid MenuShell players are neither recreated nor restarted. Main Menu remains absent throughout every failure. Invalid phase/state combinations are rejected by the pure startup state machine.

## Verification

`StartupFlowTests` covers successful order, all three phase-aware retries, Main Menu gating and invalid transition rejection without Godot. `check-startup.ps1 -GodotPath <Godot .NET executable>` verifies materialized startup media, runs the production main scene, lets the actual Splash finish, injects one failure into each recovery phase, and verifies the dependency replay, frontend setup replay, application rollback/native close safety, Main Menu gating, and exact Loader video/music player continuity across the application retry. `tools/test-frontend-media.ps1` proves unresolved Git LFS pointers are rejected; the same verifier runs in the full repository check and CI. The [frontend media record](../../assets/frontend/README.md) owns source preservation, conversion and hashes. Visual scaling, audio/video synchronization, loop-seam quality and audible output still require a rendered/manual run.

[Feature index](README.md) · [Game Menu](game-menu.md) · [Settings](settings.md) · [Arena audio](audio.md)
