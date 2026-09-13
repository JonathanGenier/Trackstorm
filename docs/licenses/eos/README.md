# Epic Online Services dependency

- **Publisher and binding:** Epic Games, official EOS C# SDK, compiled locally as `Epic.OnlineServices.dll`; no community wrapper or Unity plugin.
- **Pinned version:** `1.19.1.2-CL53289219`, the C# version returned by Epic's live SDK page on 2026-09-13. Binding constants independently identify 1.19.1.2.
- **Source:** [official SDK page](https://onlineservices.epicgames.com/sdk), [version-specific official archive](https://onlineservices.epicgames.com/api/cosmos/sdk/download?archive_id=872&archive_type=c_sharp).
- **Archive SHA-256:** `F14B93AAC2B4339492F7E1C4E0314F60F3E0CB02E176993BE8E247589D938FFF`.
- **Windows x64 native DLL SHA-256:** `9C498FE31C8DF1D9085DFA9C71C6D50EC6B569ACDB29F1DECB97BE105B6341DC`.
- **License:** proprietary [Epic Online Services Developer Agreement and applicable service addenda](https://onlineservices.epicgames.com/services/terms/agreements), not MIT or an Unreal Engine license. Epic's official binding retains its copyright headers. Review the applicable agreement in the Developer Portal before distributing builds.

`setup-eos.ps1` verifies the archive hash and extracts the official C# source, x64 native DLL and third-party notices into ignored `.godot/eos-sdk`. The full SDK archive contains optional tools, but the setup script neither extracts nor installs EAC, the EOS overlay installer, Dev Auth Tool, voice, or storefront tools. The SDK is compiled as a separate Client dependency with `EOS_PLATFORM_WINDOWS_64`. Upstream source remains unmodified. This avoids checking Epic's SDK source or a large archive into Git; a fresh checkout must run setup before building. CI does the same.

`third_party/eos/CallbackCleanup.cs` is a Trackstorm-owned extension to the official partial `Helper` class. The official wrapper retains canceled one-shot callbacks in static dictionaries after `Platform.Release`. After native release, the extension removes only registrations whose client-data owner is that released Trackstorm adapter. It never removes another owner's callbacks or rewrites the SDK's marshaling. Recheck this extension against upstream internals when upgrading the pinned SDK.

Redistribute the compiled binding, `EOSSDK-Win64-Shipping.dll`, this dependency record, and the original `ThirdPartySoftwareNotice.txt` as part of Trackstorm. MSBuild copies them to build and publish output. Do not ship SDK source or distribute SDK components as a standalone product. Section 2.1 of the agreement limits distribution to Distributable Code in object-code form as an inseparable part of the game and requires an end-user license disclaimer for Epic and its affiliates; the publisher must provide the required game EULA before external distribution. The downloaded notice file contains the bundled third-party licenses and must accompany releases.

The [official system requirements](https://dev.epicgames.com/docs/epic-online-services/system-requirements) require the Microsoft Visual C++ runtime on Windows. Install the current Microsoft **x64** Visual C++ Redistributable on tester PCs when absent. Godot's .NET Windows export also needs its normal exported executable, PCK/data and managed runtime files; distribute the entire export directory. RTC and overlays are disabled, so this identity foundation does not load XAudio2 redistributables or require the separate EOS overlay service installer. EAC is neither required nor installed.

See [development setup and verification](../../eos-development.md) for configuration security, tester setup, and export checks.
