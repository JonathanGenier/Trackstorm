# Transport dependency provenance

The Windows x64 adapter uses the standalone open-source API, without Steam accounts or the Steamworks SDK. NuGet restores pinned packages; build and publish copy runtime binaries and this license directory. Downloaded DLLs are not committed to Git.

| Component | Version / provenance | Source | License notice |
| --- | --- | --- | --- |
| GnsSharp.Gns.Win64 | 0.1.0-alpha.14 | https://github.com/nalchi-net/GnsSharp and https://www.nuget.org/packages/GnsSharp.Gns.Win64/0.1.0-alpha.14 | GnsSharp.txt, MIT |
| GameNetworkingSockets.redist | 1.6.0, x64 Release, NuGet owner Mashpoe | https://www.nuget.org/packages/GameNetworkingSockets.redist/1.6.0 and https://github.com/ValveSoftware/GameNetworkingSockets | GameNetworkingSockets.txt, BSD-3-Clause |
| OpenSSL libcrypto | 3.6.2, observed binary version | https://github.com/openssl/openssl/tree/openssl-3.6.2 | OpenSSL.txt, Apache-2.0 |
| Protobuf / Protobuf-lite | 33.4.0, observed binary version | https://github.com/protocolbuffers/protobuf/tree/v33.4 | Protobuf.txt, BSD-3-Clause |
| UTF-8 range | Embedded dependency at Protobuf v33.4 | https://github.com/protocolbuffers/protobuf/tree/v33.4/third_party/utf8_range | Utf8Range.txt, MIT |
| Abseil | Exports identify lts_20260107; patch revision not recorded by redist | https://github.com/abseil/abseil-cpp/tree/20260107.0 | Abseil.txt, Apache-2.0 |

The redist bundles dependencies rather than expressing them as separate NuGet packages. Exact shipped DLL identities (SHA-256) are below. Changing the redist requires reviewing provenance, licenses, native ABI compatibility and rerunning native plus Godot checks. The binding is prerelease; runtime checks establish the pinned pairing's compatibility.

```text
GameNetworkingSockets.dll 18172797478FB2A6883B1239E4EBA34B68CE4A772DA6A5C85DBA7E535D32D0D9
libcrypto-3-x64.dll D6A41672F2E9E3AD9C3F869D89A3D54AFD188B0F804F93E1B25887A569B16E86
libprotobuf.dll 4371417F84EBC91787FA5070C198C905C9282AADC0AB9B667D7A641D0922F6C4
libprotobuf-lite.dll 3FC6BB2EE8923FB4EDEB5807E6958415929980C47A46F4DB619EAD1F28AD7A2E
abseil_dll.dll 86A624249B03FCB014892D70654E489502CFA48803E41137DFB423366520EAD0
```

Compiler tools, the TLS library and the legacy crypto provider are not runtime requirements of this adapter and are not copied. The Windows system C/C++ runtime is a platform prerequisite of these native binaries.
