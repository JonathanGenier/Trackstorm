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
        $_ -match '\.(cs|csproj|gd|tscn|tres|blend|jsonc|js)$'
    })

    foreach ($path in $normalized) {
        if ($path -match 'TireFeedback|TireTrack|EnvironmentPresentation|EnvironmentPreset|EnvironmentSky|TerrainEffects|terrain_effects|check-terrain-effects') {
            Add-Runtime 'check-terrain-effects.ps1'
            Add-Runtime 'check-developer-options.ps1'
            Add-Manual 'Inspect all five environment packages at driving height; drive every surface, repeat tire effects and check Water splashes without persistent marks. Exercise environment replication and recovery.'
        }
        if ($path -match 'BuildDressing|infield_dressing|dressing_checks|check-dressing') {
            Add-Runtime 'check-dressing.ps1'
            Add-Runtime 'check-infield.ps1'
            Add-Runtime 'check-oval.ps1'
            Add-Manual 'Inspect dressed production map at driving height; drive routes, shoulders, jumps and two-car tunnel clearance.'
        }
        if ($path -match 'Water|water_checks|check-water' -or $path -eq 'docs/features/water.md') {
            Add-Runtime 'check-water.ps1'
            Add-Manual 'Drive repeated shallow/deep Water entry/exit and run check-death-respawn.ps1 -Water -Impaired for eight-peer lifecycle replication.'
        }
        if ($path -match '(?i)SurfaceIdentity|SurfaceField|BuildSurfaces|surface_checks|check-surfaces|surface_field|surface-sources' -or $path -eq 'docs/features/surfaces.md') {
            Add-Runtime 'check-surfaces.ps1'
            Add-Manual 'Inspect material readability, gradual shoulders, designated Water basin and local Stats during driving.'
        }
        # Feature-document-only edits stay cheap. Feature docs act as route hints only
        # when the same change also contains production/runtime files.
        if ($path -match '^docs/features/' -and -not $hasProductionChanges) {
            continue
        }

        if ($path -match '^assets/environment/' -or $path -match 'environment_library_checks|check-environment' -or $path -eq 'docs/features/environment-library.md') {
            Add-Runtime 'check-environment.ps1'
            Add-Manual 'Render check-environment.ps1 -Visual and inspect asset scale, materials, modular reuse and distance transitions.'
        }

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

        if ($path -match '^code/Client/Frontend/(HangingPlayMenu|PlayMenuSelection)' -or $path -match '^assets/frontend/play-menu/' -or $path -eq 'code/Client/Verification/PlayMenuChecks.cs') {
            Add-Runtime 'check-play-menu.ps1'
        }

        # Startup / application flow.
        if ($path -match '^code/Client/(Bootstrap|Assembly|Frontend)/' -or
            $path -match '^assets/frontend/main-menu/' -or
            $path -eq 'project.godot' -or
            $path -eq 'scenes/main.tscn' -or
            $path -eq 'docs/features/startup.md') {
            Add-Runtime 'check-startup.ps1'
        }

        # Vehicles, simulation and camera.
        if ($path -match '^code/(Core|Client)/Vehicles/' -or $path -match 'TerrainHandling|terrain_handling|check-terrain-handling') {
            Add-Runtime 'check-terrain-handling.ps1'
            Add-Manual 'Drive all normal surfaces and transitions; restart uphill on infield grades; verify host tuning and client prediction.'
        }
        if ($path -match '^code/(Core|Client)/Vehicles/' -or
            $path -eq 'docs/features/vehicles.md' -or
            $path -eq 'scenes/verification/vehicle_checks.tscn') {
            Add-Runtime 'check-vehicle.ps1'
            Add-Runtime 'check-landing.ps1'
            Add-Manual 'Drive/playtest the affected vehicle behavior, including multiple cars when collisions or shared physics are material.'
        }

        if ($path -match '^code/Core/Simulation/' -or $path -eq 'docs/features/simulation.md') {
            Add-Runtime 'check-vehicle.ps1'
            Add-Runtime 'check-landing.ps1'
            Add-Runtime 'check-match.ps1'
            Add-Manual 'Exercise sustained fixed-step gameplay and relevant multi-entity state transitions after simulation changes.'
        }

        if ($path -eq 'docs/features/camera.md' -or
            $path -match '^code/Client/.*/Camera' -or
            $path -match 'Camera.*\.cs$' -or
            $path -eq 'scenes/verification/camera_checks.tscn' -or
            $path -eq 'scenes/verification/camera_shake_playtest.tscn' -or
            $path -match '^scenes/verification/camera_obstruction_' -or
            $path -eq 'check-camera-obstruction.ps1' -or
            $path -eq 'check-camera-shake.ps1') {
            Add-Runtime 'check-camera.ps1'
            Add-Runtime 'check-camera-shake.ps1'
            Add-Runtime 'check-camera-obstruction.ps1'
            Add-Manual 'Playtest mouse/controller orbit and return, wall/terrain obstruction and clearing, camera framing, perceptible collision/damage shake and lifecycle transitions in practice and network gameplay. Run check-camera-obstruction.ps1 -Drive for native driving evidence.'
        }

        # Maps / arena.
        if ($path -match '^assets/(maps|environment)/' -or $path -match '^scenes/maps/' -or
            $path -match 'map_budget_checks|check-map-budget') {
            Add-Runtime 'check-map-budget.ps1'
        }
        if ($path -match '^code/(Core|Client)/Arenas/' -or
            $path -match '^scenes/arena/' -or
            $path -eq 'docs/features/arena.md') {
            Add-Runtime 'check-arena.ps1'
        }

        if ($path -match '^scenes/maps/' -or
            $path -eq 'docs/features/oval-map.md' -or
            $path -match '(?i)oval') {
            Add-Runtime 'check-oval.ps1'
            Add-Runtime 'check-infield.ps1'
            Add-Manual 'Drive the active map with representative multi-car traffic when map collision, scale, banking or spawn behavior changed.'
        }

        if ($path -match '(?i)infield') {
            Add-Runtime 'check-infield.ps1'
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

        if ($path -match '(?i)Nitro') { Add-Runtime 'check-nitro.ps1' }

        # Items and lifecycle.
        if ($path -match '^code/(Core|Client)/Items/' -or $path -eq 'docs/features/items.md') {
            Add-Runtime 'check-items.ps1'
            Add-Runtime 'check-oil.ps1'
            Add-Runtime 'check-nitro.ps1'
        }

        if ($path -eq 'docs/features/item-spawns.md' -or $path -match '(?i)(ItemSpawn|PickupSpawn|SpawnMarker)') {
            Add-Runtime 'check-item-spawns.ps1'
        }

        if ($path -eq 'docs/features/death-respawn.md' -or $path -match '(?i)(Death|Respawn)') {
            Add-Runtime 'check-death-respawn.ps1'
        }

        if ($path -match '^code/(Core/Matches|Tests/Matches)/Stunt' -or
            $path -match '(StuntIntegrationChecks|stunt_checks)' -or $path -eq 'docs/features/matches.md') {
            Add-Runtime 'check-stunts.ps1'
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

        if ($path -match '^code/Client/Online/' -or $path -eq 'docs/features/eos-lobbies.md') {
            Add-Runtime 'check-online-lobby.ps1'
        }

        if ($path -eq 'docs/features/eos-identity.md' -or $path -match '(?i)EosIdentity') {
            Add-Runtime 'check-eos.ps1'
        }

        if ($path -eq 'docs/features/eos-p2p.md' -or $path -match '(?i)EosP2p') {
            Add-Manual 'Run check-eos-multiplayer.ps1 with an exported executable and distinct authenticated devices when real EOS multiplayer verification is applicable.'
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

        # GDScript / GdUnit.
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
