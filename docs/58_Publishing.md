# Publishing the Windows app

Run from a PowerShell terminal with the .NET 10 SDK and Git available:

```powershell
.\Publish-CarThing.ps1
```

Exit the running target tray app first. The script does not terminate it or launch the new executable automatically.

## Workflow

1. Check that the destination EXE is not running.
2. Acquire an exclusive publisher lock and record the Git commit and working-tree status.
3. Run `tests/CarThing.Core.Tests` in Release configuration.
4. Publish a framework-dependent, single-file Windows x64 EXE into a unique staging directory (`--self-contained false`).
5. Check the output, Git status, and running process again.
6. Replace the distribution EXE and write its build record.

Tests and publishing use the current working tree, including uncommitted files. Avoid editing source files during publishing. The Git-state check detects commit and status changes; it is not a content snapshot of every source file.

## Outputs

| Path | Purpose |
|---|---|
| `dist/carthing-connector/carthing-connector.exe` | Single app file; requires the separately installed .NET 10 Desktop Runtime (x64) |
| `dist/carthing-connector.build.json` | Latest published build's source commit, dirty flag, changed paths, UTC build time, file size, and SHA256 |
| `analysis/publish/<build-id>/tests.log` | Core regression and adapter test output |
| `analysis/publish/<build-id>/publish.log` | Publish output |
| `analysis/publish/<build-id>/build-info.json` | Per-build metadata prepared before EXE replacement |
| `analysis/publish/<build-id>/staging/` | Temporary publish output |

Normal builds continue to use `bin/` and `obj/`. Run the publishing script after source changes to refresh `dist/`. The source commit alone does not identify an uncommitted build; check the dirty flag and artifact hash as well.

The existing distribution stays in place if tests or publishing fail. A replacement failure leaves the staging output available for inspection. Logs and staging directories are retained rather than deleted automatically.

## Windows runtime prerequisite

Install **.NET 10 Desktop Runtime, Windows x64**, from [Microsoft's download page](https://dotnet.microsoft.com/en-us/download/dotnet/10.0), then run the EXE. The SDK is only needed for development. The plain .NET Runtime and ASP.NET Core Runtime alone do not include the Windows desktop framework used by this app.

The application and its libraries remain bundled in one EXE; the .NET runtime is no longer bundled. Windows can show the standard .NET installation prompt if the required runtime is missing. Install the Desktop Runtime and reopen the app. Build metadata records `selfContained: false` and the required runtime.

## Script validation

| Condition | Result |
|---|---|
| Running destination EXE | Rejected before tests or publishing |
| Simulated test failure in an isolated fixture | Existing EXE hash preserved |
| Simulated publish failure in an isolated fixture | Existing EXE hash preserved |
| Simulated successful publish in an isolated fixture | EXE replaced; metadata hash matched the replacement |

Fixture results: `analysis/publish-script-tests.log`.


## Prepare two preview downloads

Publish to a separate directory to leave an existing running installation unchanged:

```powershell
.\Publish-CarThing.ps1 -OutputDirectory dist/release
```

The output folder contains `carthing-connector.exe`; its build record is written beside the folder as `dist/release.build.json`. The script checks the destination path against running processes and does not stop any process.

Build the Android Release preview using the command below, then copy the signed APK to `dist/release/carthing-connector-android-preview.apk`. This preserves compatibility with the existing development-key installation. The APK is non-debuggable, ARM64-only, and remains a testing preview rather than a production-signed store release.

```powershell
dotnet publish android-connector/CarThing.Android.csproj -c Release -r android-arm64 `
  "-p:AndroidSdkDirectory=$env:LOCALAPPDATA\Android\Sdk" `
  "-p:JavaSdkDirectory=$env:LOCALAPPDATA\Android\jdk" `
  -p:PublishTrimmed=false -p:RunAOTCompilation=false -p:AndroidEnableProfiledAot=false `
  -p:JsonSerializerIsReflectionEnabledByDefault=true -o analysis/android-release-staging

Copy-Item analysis/android-release-staging/io.github.carthingconnector.android-Signed.apk `
  dist/release/carthing-connector-android-preview.apk -Force
```

The default local Android development key must match the previous installation for an in-place update. Keep that key private and backed up; do not commit or distribute it. Trimming and AOT are disabled for this preview to preserve the current reflection-based protocol serialization. Android bundles its own runtime; users do not install .NET separately on their phone.

`AndroidEnableMarshalMethods=false` is set in the Android project to avoid a Release startup crash resolving `MauiApplication.n_onCreate()`. When switching this setting on an existing build tree, clean the Android Release target before publishing to regenerate its Java wrappers. See [MAUI issue 35209](https://github.com/dotnet/maui/issues/35209).

Device verification after this correction: the Release APK launched on Samsung SM-F946N, its process remained alive, and the UI reported `Connected to Car Thing (SN: Q61S)`. Evidence: `analysis/android-release-crash/process-after.log` and `window-after.xml`; the corrected APK replaced the distribution copy.

Upload these two binaries as GitHub Release assets. Generated EXEs, APKs, and build records remain ignored by Git; commit the source and documentation instead.

## Release preview build verification (2026-10-27)

| Artifact | Previous bytes | Current bytes | Configuration |
|---|---:|---:|---|
| Windows EXE | 142,472,888 | 26,576,414 | Release, win-x64, single file, external Desktop Runtime |
| Android APK | 84,472,175 | 35,837,145 | Release, ARM64, non-debuggable, existing development signature |

- Core regression and adapter tests passed; published EXE tray self-tests passed using the installed Desktop Runtime.
- Android APK v2/v3 signatures verified; signing certificate SHA256 matches the previous preview. Manifest confirms minimum API 26 and ARM64-only native code. JSON reflection remains enabled.
- Windows build record: `dist/release.build.json`; Android build record: `dist/release.android.build.json`.
- Verification logs: `analysis/release-framework-tray-test.log`, `analysis/android-release-build.log`, `analysis/android-release-signature.txt`, and `analysis/android-release-manifest.txt`.
