# Client Agent Instructions

Client owns Godot integration, presentation, local input capture, UI, camera, audio, animation, effects, and interpolation. Client may depend on Core and use intentional Core APIs, but must not duplicate authoritative rules or become a second source of game state. Keep Client objects reconstructable from Core state where practical.
