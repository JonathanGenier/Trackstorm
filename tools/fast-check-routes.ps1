function Get-FastCheckPlan {
    param(
        [Parameter(Mandatory)]
        [string[]]$Paths
    )

    $normalized = @($Paths | ForEach-Object { $_.Replace('\', '/') })

    $coreTests = $false
    $transportTests = $false
    $serviceTests = $false
    $clientBuild = $false
    $versionChecks = $false
    $mediaChecks = $false
    $runtime = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $extended = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $manual = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

    function Add-Runtime([string]$Script) {
        [void]$runtime.Add($Script)
    }

    function Add-Extended([string]$Script) {
        [void]$extended.Add($Script)
    }

    function Add-Manual([string]$Scenario) {
        [void]$manual.Add($Scenario)
    }

    $hasProductionChanges = [bool]($normalized | Where-Object {
        $_ -match '^code/' -or
        $_ -match '^test/' -or
        $_ -match '^services/' -or
        $_ -match '^scenes/' -or
        $_ -match '^assets/' -or
        $_ -eq 'project.godot' -or
        $_ -match '\.(cs|csproj|gd|tscn|tres|blend|jsonc|js)
        if ($path -match '^code/Core/' -or $path -match '^code/Tests/') {
            $coreTests = $true
        }

        if ($path -match '^code/Client/' -or
            $path -match '^test/Client/' -or
            $path -match '^scenes/' -or
            $path -match '^assets/' -or
            $path -eq 'project.godot' -or
            $path -eq 'Trackstorm.Client.csproj') {
            $clientBuild = $true
        }

        if ($path -match '^services/authority-lease/') {
            $serviceTests = $true
        }

        if ($path -match '^code/TransportTests/' -or
            $path -match '^code/(Core|Client)/Networking/' -or
            $path -match '^code/Client/Online/' -or
            $path -eq 'docs/features/transport.md' -or
            $path -eq 'docs/features/vehicle-networking.md' -or
            $path -eq 'docs/features/eos-p2p.md') {
            $transportTests = $true
        }

        if ($path -eq 'Directory.Build.props' -or
            $path -eq 'export_presets.cfg' -or
            $path -match '^tools/.*version.*\.ps1$' -or
            $path -eq '.github/version-transition.json') {
            $versionChecks = $true
        }

        if ($path -match '^assets/frontend/' -or $path -match '^tools/.*frontend-media.*\.ps1$') {
            $mediaChecks = $true
        }

        # A feature-document-only correction does not justify runtime execution by itself.
        # Feature docs still act as routing hints when the same change includes production/runtime files.
        if ($path -match '^docs/features/' -and -not $hasProductionChanges) {
            continue
        }

        # Startup / application flow.
        if ($path -match '^code/Client/(Bootstrap|Assembly)/' -or
            $path -eq 'project.godot' -or
            $path -eq 'scenes/main.tscn' -or
            $path -eq 'docs/features/startup.md') {
            Add-Runtime 'check-startup.ps1'
        }

        # Vehicle, physics, fixed-step simulation and camera-facing integration.
        if ($path -match '^code/(Core|Client)/Vehicles/' -or
            $path -eq 'docs/features/vehicles.md' -or
            $path -match '^scenes/verification/vehicle_checks\.tscn$') {
            Add-Runtime 'check-vehicle.ps1'
            Add-Manual 'Drive/playtest the affected vehicle behavior, including multiple cars when collisions or shared physics are material.'
        }

        if ($path -match '^code/Core/Simulation/' -or $path -eq 'docs/features/simulation.md') {
            Add-Runtime 'check-vehicle.ps1'
            Add-Runtime 'check-match.ps1'
            Add-Manual 'Exercise sustained fixed-step gameplay and relevant multi-entity state transitions after simulation changes.'
        }

        if ($path -eq 'docs/features/camera.md' -or
            $path -match '^code/Client/.*/Camera' -or
            $path -match 'Camera.*\.cs$' -or
            $path -eq 'scenes/verification/camera_checks.tscn') {
            Add-Manual 'Playtest camera framing, obstruction/clipping, responsiveness and transitions; no dedicated root camera check script exists.'
        }

        # Maps / arena.
        if ($path -match '^code/(Core|Client)/Arenas/' -or
            $path -match '^scenes/arena/' -or
            $path -eq 'docs/features/arena.md') {
            Add-Runtime 'check-arena.ps1'
        }

        if ($path -match '^scenes/maps/' -or
            $path -eq 'docs/features/oval-map.md' -or
            $path -match '(?i)oval') {
            Add-Runtime 'check-oval.ps1'
            Add-Manual 'Drive the active map with representative multi-car traffic when map collision, scale, banking or spawn behavior changed.'
        }

        # Input, settings, menu and HUD.
        if ($path -match '^code/(Core|Client)/Input/' -or $path -eq 'docs/features/input.md') {
            Add-Runtime 'check-input.ps1'
        }

        if ($path -match '^code/(Core|Client)/Settings/' -or $path -eq 'docs/features/settings.md') {
            Add-Runtime 'check-settings.ps1'
        }

        if ($path -match '^code/Client/Hud/' -or $path -eq 'docs/features/hud.md') {
            Add-Runtime 'check-hud.ps1'
            Add-Manual 'Visually inspect affected HUD layout/readability at relevant resolutions.'
        }

        if ($path -eq 'docs/features/game-menu.md' -or $path -match '(?i)(MenuShell|GameMenu|EscapeMenu|MainMenu)') {
            Add-Runtime 'check-menu.ps1'
            Add-Manual 'Navigate the affected menu with mouse/keyboard/controller paths that are material to the change.'
        }

        # Items and lifecycle.
        if ($path -match '^code/(Core|Client)/Items/' -or $path -eq 'docs/features/items.md') {
            Add-Runtime 'check-items.ps1'
        }

        if ($path -eq 'docs/features/item-spawns.md' -or $path -match '(?i)(ItemSpawn|PickupSpawn|SpawnMarker)') {
            Add-Runtime 'check-item-spawns.ps1'
        }

        if ($path -eq 'docs/features/death-respawn.md' -or $path -match '(?i)(Death|Respawn)') {
            Add-Runtime 'check-death-respawn.ps1'
        }

        # Match lifecycle / standings.
        if ($path -match '^code/Core/Matches/' -or
            $path -eq 'docs/features/matches.md' -or
            $path -eq 'docs/features/game-loop.md') {
            Add-Runtime 'check-match.ps1'
        }

        if ($path -eq 'docs/features/standings.md' -or $path -match '(?i)Standing') {
            Add-Runtime 'check-standings.ps1'
        }

        # Sessions, lobby, reconnect and migration.
        if ($path -match '^code/Core/Sessions/' -or
            $path -eq 'docs/features/sessions.md' -or
            $path -eq 'docs/features/match-entry.md' -or
            $path -match '(?i)Lobby') {
            Add-Runtime 'check-lobby.ps1'
        }

        if ($path -eq 'docs/features/reconnection.md' -or $path -match '(?i)Reconnect') {
            Add-Runtime 'check-reconnect.ps1'
        }

        if ($path -eq 'docs/features/host-migration.md' -or $path -match '(?i)Migration') {
            Add-Runtime 'check-migration.ps1'
            Add-Extended 'check-migration-processes.ps1'
        }

        # Networking / online.
        if ($path -match '^code/(Core|Client)/Networking/' -or
            $path -eq 'docs/features/vehicle-networking.md') {
            Add-Runtime 'check-network-vehicles.ps1'
            Add-Manual 'Exercise multiple peers/cars under representative latency when synchronization, prediction or reconciliation changed.'
        }

        if ($path -match '^code/Client/Online/' -or
            $path -eq 'docs/features/eos-lobbies.md') {
            Add-Runtime 'check-online-lobby.ps1'
        }

        if ($path -eq 'docs/features/eos-identity.md' -or $path -match '(?i)EosIdentity') {
            Add-Runtime 'check-eos.ps1'
        }

        if ($path -eq 'docs/features/eos-p2p.md') {
            Add-Extended 'check-eos-multiplayer.ps1'
        }

        # DevTools.
        if ($path -eq 'docs/features/devtools.md' -or $path -match '(?i)DevTools') {
            Add-Runtime 'check-developer-options.ps1'
            Add-Runtime 'check-statistics.ps1'
            Add-Runtime 'check-event-log.ps1'
        }

        if ($path -eq 'docs/features/developer-options.md' -or $path -match '(?i)DeveloperOptions') {
            Add-Runtime 'check-developer-options.ps1'
        }

        if ($path -match '^code/Client/Statistics/' -or
            $path -eq 'docs/features/statistics.md' -or
            $path -match '(?i)Statistic') {
            Add-Runtime 'check-statistics.ps1'
        }

        if ($path -match '^code/Core/Events/' -or
            $path -eq 'docs/features/event-log.md' -or
            $path -match '(?i)EventLog') {
            Add-Runtime 'check-event-log.ps1'
        }

        if ($path -eq 'docs/features/activity-feed.md' -or $path -match '(?i)ActivityFeed') {
            Add-Runtime 'check-event-log.ps1'
            Add-Runtime 'check-hud.ps1'
        }

        # GDScript / GdUnit client tests and import wrapper behavior.
        if ($path -match '^test/Client/' -or $path -match '^addons/gdUnit4/') {
            Add-Runtime 'check-gdunit.ps1'
        }

        if ($path -eq 'import-godot.ps1' -or
            $path -eq 'check-gdunit.ps1' -or
            $path -eq 'check-gdunit-regression.ps1') {
            Add-Extended 'check-gdunit-regression.ps1'
        }

        # Audio.
        if ($path -match '^code/Client/Audio/' -or
            $path -eq 'docs/features/audio.md' -or
            $path -match '^assets/.*/(?i:audio|music|sound)/') {
            Add-Runtime 'check-audio.ps1'
            Add-Manual 'Listen to the affected audio in gameplay context for timing, balance, repetition and usefulness.'
        }
    }

    # Any C#/project change not already clearly covered still gets a Client compile safety net.
    if ($normalized | Where-Object { $_ -match '\.cs$' -or $_ -match '\.csproj$' -or $_ -match '\.props$' }) {
        $clientBuild = $true
    }

    [pscustomobject]@{
        CoreTests = $coreTests
        TransportTests = $transportTests
        ServiceTests = $serviceTests
        ClientBuild = $clientBuild
        VersionChecks = $versionChecks
        MediaChecks = $mediaChecks
        RuntimeScripts = @($runtime | Sort-Object)
        ExtendedScripts = @($extended | Sort-Object)
        ManualScenarios = @($manual | Sort-Object)
    }
}

    })

    foreach ($path in $normalized) {
        if ($path -match '^code/Core/' -or $path -match '^code/Tests/') {
            $coreTests = $true
        }

        if ($path -match '^code/Client/' -or
            $path -match '^test/Client/' -or
            $path -match '^scenes/' -or
            $path -match '^assets/' -or
            $path -eq 'project.godot' -or
            $path -eq 'Trackstorm.Client.csproj') {
            $clientBuild = $true
        }

        if ($path -match '^code/TransportTests/' -or
            $path -match '^code/(Core|Client)/Networking/' -or
            $path -match '^code/Client/Online/' -or
            $path -eq 'docs/features/transport.md' -or
            $path -eq 'docs/features/vehicle-networking.md' -or
            $path -eq 'docs/features/eos-p2p.md') {
            $transportTests = $true
        }

        if ($path -eq 'Directory.Build.props' -or
            $path -eq 'export_presets.cfg' -or
            $path -match '^tools/.*version.*\.ps1$' -or
            $path -eq '.github/version-transition.json') {
            $versionChecks = $true
        }

        if ($path -match '^assets/frontend/' -or $path -match '^tools/.*frontend-media.*\.ps1$') {
            $mediaChecks = $true
        }

        # Startup / application flow.
        if ($path -match '^code/Client/(Bootstrap|Assembly)/' -or
            $path -eq 'project.godot' -or
            $path -eq 'scenes/main.tscn' -or
            $path -eq 'docs/features/startup.md') {
            Add-Runtime 'check-startup.ps1'
        }

        # Vehicle, physics, fixed-step simulation and camera-facing integration.
        if ($path -match '^code/(Core|Client)/Vehicles/' -or
            $path -eq 'docs/features/vehicles.md' -or
            $path -match '^scenes/verification/vehicle_checks\.tscn$') {
            Add-Runtime 'check-vehicle.ps1'
            Add-Manual 'Drive/playtest the affected vehicle behavior, including multiple cars when collisions or shared physics are material.'
        }

        if ($path -match '^code/Core/Simulation/' -or $path -eq 'docs/features/simulation.md') {
            Add-Runtime 'check-vehicle.ps1'
            Add-Runtime 'check-match.ps1'
            Add-Manual 'Exercise sustained fixed-step gameplay and relevant multi-entity state transitions after simulation changes.'
        }

        if ($path -eq 'docs/features/camera.md' -or
            $path -match '^code/Client/.*/Camera' -or
            $path -match 'Camera.*\.cs$' -or
            $path -eq 'scenes/verification/camera_checks.tscn') {
            Add-Manual 'Playtest camera framing, obstruction/clipping, responsiveness and transitions; no dedicated root camera check script exists.'
        }

        # Maps / arena.
        if ($path -match '^code/(Core|Client)/Arenas/' -or
            $path -match '^scenes/arena/' -or
            $path -eq 'docs/features/arena.md') {
            Add-Runtime 'check-arena.ps1'
        }

        if ($path -match '^scenes/maps/' -or
            $path -eq 'docs/features/oval-map.md' -or
            $path -match '(?i)oval') {
            Add-Runtime 'check-oval.ps1'
            Add-Manual 'Drive the active map with representative multi-car traffic when map collision, scale, banking or spawn behavior changed.'
        }

        # Input, settings, menu and HUD.
        if ($path -match '^code/(Core|Client)/Input/' -or $path -eq 'docs/features/input.md') {
            Add-Runtime 'check-input.ps1'
        }

        if ($path -match '^code/(Core|Client)/Settings/' -or $path -eq 'docs/features/settings.md') {
            Add-Runtime 'check-settings.ps1'
        }

        if ($path -match '^code/Client/Hud/' -or $path -eq 'docs/features/hud.md') {
            Add-Runtime 'check-hud.ps1'
            Add-Manual 'Visually inspect affected HUD layout/readability at relevant resolutions.'
        }

        if ($path -eq 'docs/features/game-menu.md' -or $path -match '(?i)(MenuShell|GameMenu|EscapeMenu|MainMenu)') {
            Add-Runtime 'check-menu.ps1'
            Add-Manual 'Navigate the affected menu with mouse/keyboard/controller paths that are material to the change.'
        }

        # Items and lifecycle.
        if ($path -match '^code/(Core|Client)/Items/' -or $path -eq 'docs/features/items.md') {
            Add-Runtime 'check-items.ps1'
        }

        if ($path -eq 'docs/features/item-spawns.md' -or $path -match '(?i)(ItemSpawn|PickupSpawn|SpawnMarker)') {
            Add-Runtime 'check-item-spawns.ps1'
        }

        if ($path -eq 'docs/features/death-respawn.md' -or $path -match '(?i)(Death|Respawn)') {
            Add-Runtime 'check-death-respawn.ps1'
        }

        # Match lifecycle / standings.
        if ($path -match '^code/Core/Matches/' -or
            $path -eq 'docs/features/matches.md' -or
            $path -eq 'docs/features/game-loop.md') {
            Add-Runtime 'check-match.ps1'
        }

        if ($path -eq 'docs/features/standings.md' -or $path -match '(?i)Standing') {
            Add-Runtime 'check-standings.ps1'
        }

        # Sessions, lobby, reconnect and migration.
        if ($path -match '^code/Core/Sessions/' -or
            $path -eq 'docs/features/sessions.md' -or
            $path -eq 'docs/features/match-entry.md' -or
            $path -match '(?i)Lobby') {
            Add-Runtime 'check-lobby.ps1'
        }

        if ($path -eq 'docs/features/reconnection.md' -or $path -match '(?i)Reconnect') {
            Add-Runtime 'check-reconnect.ps1'
        }

        if ($path -eq 'docs/features/host-migration.md' -or $path -match '(?i)Migration') {
            Add-Runtime 'check-migration.ps1'
            Add-Extended 'check-migration-processes.ps1'
        }

        # Networking / online.
        if ($path -match '^code/(Core|Client)/Networking/' -or
            $path -eq 'docs/features/vehicle-networking.md') {
            Add-Runtime 'check-network-vehicles.ps1'
            Add-Manual 'Exercise multiple peers/cars under representative latency when synchronization, prediction or reconciliation changed.'
        }

        if ($path -match '^code/Client/Online/' -or
            $path -eq 'docs/features/eos-lobbies.md') {
            Add-Runtime 'check-online-lobby.ps1'
        }

        if ($path -eq 'docs/features/eos-identity.md' -or $path -match '(?i)EosIdentity') {
            Add-Runtime 'check-eos.ps1'
        }

        if ($path -eq 'docs/features/eos-p2p.md') {
            Add-Extended 'check-eos-multiplayer.ps1'
        }

        # DevTools.
        if ($path -eq 'docs/features/devtools.md' -or $path -match '(?i)DevTools') {
            Add-Runtime 'check-developer-options.ps1'
            Add-Runtime 'check-statistics.ps1'
            Add-Runtime 'check-event-log.ps1'
        }

        if ($path -eq 'docs/features/developer-options.md' -or $path -match '(?i)DeveloperOptions') {
            Add-Runtime 'check-developer-options.ps1'
        }

        if ($path -match '^code/Client/Statistics/' -or
            $path -eq 'docs/features/statistics.md' -or
            $path -match '(?i)Statistic') {
            Add-Runtime 'check-statistics.ps1'
        }

        if ($path -match '^code/Core/Events/' -or
            $path -eq 'docs/features/event-log.md' -or
            $path -match '(?i)EventLog') {
            Add-Runtime 'check-event-log.ps1'
        }

        if ($path -eq 'docs/features/activity-feed.md' -or $path -match '(?i)ActivityFeed') {
            Add-Runtime 'check-event-log.ps1'
            Add-Runtime 'check-hud.ps1'
        }

        # GDScript / GdUnit client tests and import wrapper behavior.
        if ($path -match '^test/Client/' -or $path -match '^addons/gdUnit4/') {
            Add-Runtime 'check-gdunit.ps1'
        }

        if ($path -eq 'import-godot.ps1' -or
            $path -eq 'check-gdunit.ps1' -or
            $path -eq 'check-gdunit-regression.ps1') {
            Add-Extended 'check-gdunit-regression.ps1'
        }

        # Audio.
        if ($path -match '^code/Client/Audio/' -or
            $path -eq 'docs/features/audio.md' -or
            $path -match '^assets/.*/(?i:audio|music|sound)/') {
            Add-Runtime 'check-audio.ps1'
            Add-Manual 'Listen to the affected audio in gameplay context for timing, balance, repetition and usefulness.'
        }
    }

    # Any C#/project change not already clearly covered still gets a Client compile safety net.
    if ($normalized | Where-Object { $_ -match '\.cs$' -or $_ -match '\.csproj$' -or $_ -match '\.props$' }) {
        $clientBuild = $true
    }

    [pscustomobject]@{
        CoreTests = $coreTests
        TransportTests = $transportTests
        ClientBuild = $clientBuild
        VersionChecks = $versionChecks
        MediaChecks = $mediaChecks
        RuntimeScripts = @($runtime | Sort-Object)
        ExtendedScripts = @($extended | Sort-Object)
        ManualScenarios = @($manual | Sort-Object)
    }
}
