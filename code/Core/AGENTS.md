# Core Agent Instructions

Core is authoritative, engine-independent, and independently unit-testable. Do not reference Client, Godot, nodes, scenes, rendering, UI, camera, audio, local input, or scene-tree lifecycle. Prefer plain C#, deterministic behavior, explicit validation, and controllable external seams. Keep APIs intentional and add NUnit tests for authoritative behavior changes.
