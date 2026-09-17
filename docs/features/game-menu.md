# In-game Game Menu and Settings navigation

Escape opens a centered overlay whenever an arena exists, including a lone player in `MatchPhase.Waiting`, countdown, Active and Finished. The existing remappable Pause action (P / gamepad Start by default) also opens it. Escape, logical Cancel or Pause goes Back one level; at the top level it closes. Back to Game closes directly. The gamepad B/handbrake binding does not open the overlay during driving.

The hierarchy is Game Menu → Settings → Audio, Video, Gameplay, Interface, Controls or Developer Options. Category Back returns to Settings; Settings Back returns to Game Menu. Settings opened from the main menu returns to its caller. [Developer Options](developer-options.md) uses the existing category container for host tuning/actions and read-only diagnostics. F1 directly opens/closes that same page in development builds. Numeric editors retain native text entry; Escape still goes Back and controller navigation can leave the editor.

## Ownership and input

The full-screen [Statistic Panel](statistics.md) can cover the menu with F2.
While it is open, underlying menu navigation is suspended. Closing it restores
the prior menu focus and keeps gameplay input suppressed if the menu is still open.

`SettingsPanel` composes the existing preference controls with `MenuNavigation`, a pure Client back stack. `SettingsPanel.Navigation` routes the existing input owner's logical bindings to focus, buttons, sliders and option selections. Directional input repeats after 0.4 seconds at 0.12-second intervals. Binding capture consumes the candidate input and cancels on Escape. Native `ui_*` handling is consumed while the overlay is open so default controls cannot double-activate or bypass remapping. Escape remains a reserved access/cancel key even if Cancel is rebound. Focused controls scroll into view; Back remains outside the scroll area.

Only `PlayerInputAdapter.GameplaySuppressed` changes. The scene tree is never paused, and fixed simulation, native physics, remote inputs, networking and audio continue. Navigation samples the existing binding owner independently of gameplay-frame suppression. Closing restores local control; the adapter's existing item-release guard prevents a held Accept/Use Item button from firing an item immediately afterward.

## Scope and persistence

| Category | Existing preference owner |
| --- | --- |
| Audio | Master, Music and SFX gains through `PlayerSettingsController` and existing buses |
| Video | Windowed/fullscreen and supported window resolutions; existing 15-second preview/Keep/Revert semantics |
| Gameplay | km/h or mph, presentation only |
| Interface | Independent Show FPS and Show Ping flags |
| Controls | All existing logical-action remaps and Invert steering; binding capture/clear/restore uses `PlayerInputBindings` |
| Developer Options | Host-authoritative gameplay tuning/actions with separate host-local persistence; safe read-only diagnostics |

The existing local settings model, codec, debounced file store, display application and audio routing remain authoritative for user preferences. Developer tuning does not extend that schema. Analog dead zone remains an existing persisted input configuration, but is not offered as an additional menu control. Each persistence owner exposes its own save status/retry: developer tuning retries through **Apply Settings**, while Discard and Reset only change its editor draft. See [settings](settings.md) for local storage and preview guarantees.

## Leave and quit

The arena has no permanent Settings, End Session/Return or Leave button. Final standings likewise points to ESC instead of duplicating a session action. The lobby retains its own Leave control. Game Menu Leave to Main Menu calls `DevelopmentSession.Leave`, retaining reliable client departure delivery, transport release, online membership cancellation and saved-resume clearing. Re-entry constructs a new arena. The local development practice arena is released before displaying the existing multiplayer main menu.

Game Menu Quit and the window close request share the bootstrap's exit path. They request the same session Leave and wait for session and online membership cleanup, while pumping normal runtime callbacks. An online cleanup failure remains visible with a retryable Quit action. Successful cleanup proceeds through normal tree teardown: settings flush, native transport disposal, EOS platform release and terminal SDK shutdown retain their existing owners. This branch closes the session when the host leaves; host migration is not implemented here. The menu introduces no replacement election or migration path.

## Presentation and verification

`MenuPresentation` owns the shared steel texture treatment, procedural scratches/rivets/chains/spikes, striped carnival canopy, clown crest and red focus theme. The four project-supplied TS-61 mockups (`ESC Scene.png`, `Game Menu.png`, `Settings Menu.png`, `Audio menu.png`) guide this composition. Artwork is independent of navigation, and every row remains a native interactive Control. The existing HUD steel sample is reused; no new external asset or font is required. A translucent shade de-emphasizes the running arena. The design scales uniformly inside the viewport; the top-level menu uses a shorter frame than category pages.

`check-menu.ps1 -GodotPath <Godot .NET executable>` exercises the production bootstrap with isolated settings storage and real local UDP sessions. It covers solo Waiting, ESC/Back, synthetic keyboard/gamepad navigation, remapping, local suppression with continued authoritative and remote progress, setting updates/persistence, unified Developer Options, retired buttons, viewport bounds, Leave/re-entry and production Quit. `-Visual` also captures all categories and four viewport sizes. `check-settings.ps1 -Visual` separately verifies restart persistence, save failure/retry and display preview/confirmation/reversion. Lobby and standings harnesses retain direct coverage of the underlying Return command without recreating the retired UI. Native synthetic input is not evidence of physical controller ergonomics or separate-PC authenticated EOS behavior.

[Feature index](README.md) · [Input](input.md) · [Sessions](sessions.md) · [HUD](hud.md) · [Standings](standings.md)
