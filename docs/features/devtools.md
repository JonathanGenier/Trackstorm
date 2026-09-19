# DevTools shell

DevTools is one full-window Client shell with **Configs**, **Stats** and **Logs** tabs. F1 opens or selects Configs, F2 opens or selects Stats, and F3 opens or selects Logs. A shortcut pressed while the shell is visible changes the selected tab without reconstructing or closing the shell. Repeating the active shortcut leaves it open. Escape and the persistent Close button dismiss the shell.

The shell owns only presentation, tab selection, focus restoration and the independent diagnostic input-suppression gate. It never pauses the scene tree, reloads an arena or recreates a session, so simulation and networking continue while it is open. When opened above the Game Menu or Settings, closing DevTools restores the prior menu focus and leaves the menu's own gameplay suppression intact. Settings > Developer Options routes to the same Configs tab instead of creating another panel.

## Tab ownership and policy

The existing `PlayerInputBindings` drive logical Up/Down, Left/Right, Accept and Cancel, including remapped keys, controller buttons and signed axes. DevTools and Settings share focus/control activation through `MenuFocusNavigation`; directional input repeats after 0.4 seconds at 0.12-second intervals. Directions move out of numeric text editors (including SpinBox's inner editor), while other keyboard text entry remains native. Accept does not apply a numeric draft. Cancel or Pause closes the shell; held closing input is consumed before the underlying menu resumes, preventing a second Back action. F1/F2/F3 and Escape remain direct shortcuts.

| Tab | Existing authoritative implementation | Access contract |
| --- | --- | --- |
| Configs | `DeveloperOptionsPanel`, `DeveloperOptionsDraft`, `DevelopmentSession` and the existing Core configuration validators | Editable configuration and authorized developer actions; only the current host can see or invoke mutations. |
| Stats | `RuntimeStatistics`, the existing safe `DeveloperDiagnostics` projection and `StatisticPanel` | Read-only current runtime state and telemetry, including Network Diagnostics previously shown in Developer Options. |
| Logs | `EventStream`, replication and `EventLogPanel` | Read-only bounded history. |

The common shell does not own configuration, telemetry or journal state. Configs contains mutations, Stats answers what is true now, and Logs records what happened in order. The Network Diagnostics presentation is not duplicated in Configs. Tab changes therefore preserve drafts, selection/filter state and existing backend lifetimes. There is exactly one shell in the production bootstrap.

The existing build policy remains split by capability. F2 Stats and F3 Logs remain available in Debug and exported Release builds. Configs and its F1 entry follow `TrackstormDeveloperTools`; when that capability is disabled, Configs cannot be selected while the read-only tabs remain available.

## Presentation and verification

Configs uses a centered content area capped at 640 pixels, shrinking with the viewport. Gameplay and local network configuration grids share a 280-pixel wrapping label column, a 16-pixel gap and a practical 150-pixel value column. Vertical scrolling follows focus, including at 640×360; row labels never expand across wide displays. Layout does not change configuration or Apply/Discard/Reset semantics.

The shell fills the viewport with a black/dark-gray panel, a fixed header containing readable tab buttons and a persistent Close button, and one visible content surface. Configs scrolls inside the common content area; Stats and Logs keep their existing internal controls.

The menu, Developer Options, statistics and event-log native harnesses jointly exercise shortcut routing, in-place switching, single-shell composition, Escape/Close, focus and gameplay suppression, live gameplay continuity, host-only configuration, read-only diagnostics and viewport bounds. Debug/Release build checks preserve the compile-time availability policy.

[Feature index](README.md) · [Developer Options](developer-options.md) · [Statistic Panel](statistics.md) · [Event Log](event-log.md) · [Game Menu](game-menu.md)
