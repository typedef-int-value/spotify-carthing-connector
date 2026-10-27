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
4. Publish a self-contained Windows x64 EXE into a unique staging directory.
5. Check the output, Git status, and running process again.
6. Replace the distribution EXE and write its build record.

Tests and publishing use the current working tree, including uncommitted files. Avoid editing source files during publishing. The Git-state check detects commit and status changes; it is not a content snapshot of every source file.

## Outputs

| Path | Purpose |
|---|---|
| `dist/carthing-connector/carthing-connector.exe` | The only file needed to run the app |
| `dist/carthing-connector.build.json` | Latest published build's source commit, dirty flag, changed paths, UTC build time, file size, and SHA256 |
| `analysis/publish/<build-id>/tests.log` | Core regression and adapter test output |
| `analysis/publish/<build-id>/publish.log` | Publish output |
| `analysis/publish/<build-id>/build-info.json` | Per-build metadata prepared before EXE replacement |
| `analysis/publish/<build-id>/staging/` | Temporary publish output |

Normal builds continue to use `bin/` and `obj/`. Run the publishing script after source changes to refresh `dist/`. The source commit alone does not identify an uncommitted build; check the dirty flag and artifact hash as well.

The existing distribution stays in place if tests or publishing fail. A replacement failure leaves the staging output available for inspection. Logs and staging directories are retained rather than deleted automatically.

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

Build the Android preview using the [Android build instructions](60_Android_Connector.md), then copy the signed Debug APK to `dist/release/carthing-connector-android-preview.apk`. This preserves compatibility with the existing development-key installation. The APK is debuggable and is a testing preview, not a production-signed store release.

Upload these two binaries as GitHub Release assets. Generated EXEs, APKs, and build records remain ignored by Git; commit the source and documentation instead.
