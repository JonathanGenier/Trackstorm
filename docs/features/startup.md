# Startup application flow

Fresh production startup follows one explicit sequence:

`Preloader → Splash Screen → MenuShell / Loader → Main Menu`

`SimulationBootstrap` remains the application composition root. The main scene enables its startup controller; isolated integration fixtures construct the same bootstrap without replaying presentation delays. Command-line verification entry points continue to run before the product startup sequence so their existing ownership and timing remain unchanged.

## Ownership and transitions

`StartupFlow` accepts only the required ordered transitions. The Preloader lasts one presented frame and creates only the dedicated `SplashScreen`; it performs no global application initialization. After the splash completes, `StartupController` creates one `MenuShell` and never replaces it while moving from Loader to Main Menu.

`MenuShell` owns the animated frontend background, loader/failure chrome and separately routed frontend music player. The current background is procedural because the project has no committed frontend video asset. A future video can replace that visual owner without embedding its audio: frontend music remains an independent looping `AudioStreamPlayer` on the settings-controlled Music bus. Entering gameplay hides the shell and stops its music; returning to the frontend resumes the same shell owner.

The existing multiplayer menu and Settings entry are composed hidden while the Loader is visible. Successful initialization fades those existing controls in while fading Loader chrome out. The background node is neither restarted nor recreated during this transition.

## Application initialization

The Loader retains the reusable resources that existing menu and gameplay presentation already require: common HUD textures/icons, shared HUD and damage shaders, the current music tracks, ambience, vehicle loops, combat cues and UI cues. Resources load incrementally across frames before the existing application composition creates settings/audio buses, diagnostics, HUD systems, EOS identity where enabled, and the multiplayer Main Menu/session owner.

Loading or required-system composition failure enters a blocking failure panel inside the same MenuShell. Main Menu remains absent. Retry clears the partial resource set and reruns the Loader; Quit exits with failure. Invalid state skips are rejected by the pure startup state machine.

## Verification

`StartupFlowTests` covers successful order, failure/retry and invalid transition rejection without Godot. `check-startup.ps1 -GodotPath <Godot .NET executable>` runs the production main scene, injects one required-initialization failure, retries through the production recovery path, and verifies ordered states, splash-before-shell, Main Menu gating and exact MenuShell background instance continuity. Visual timing, animation quality and audible output still require a rendered/manual run.

[Feature index](README.md) · [Game Menu](game-menu.md) · [Settings](settings.md) · [Arena audio](audio.md)
