# Android connector

The Android app uses MAUI for its setup screen and `CarThing.Core` for the Bluetooth companion protocol. Its media source is the phone's active Android media session.

## Requirements and installation

- Android 8.0 / API 26 or later with Bluetooth Classic support.
- A stock Car Thing paired with the phone in Android Bluetooth settings.
- Nearby devices permission on Android 12 and later.
- Notification listener access for querying other apps' media sessions.
- Notification permission on Android 13 and later for the connection notification to appear in the notification drawer.

Development APK: `dist/android/carthing-connector-debug.apk`. This APK includes the managed assemblies and uses the local Android debug signing key.

1. Install the APK on the phone. Android may ask you to allow installation from the chosen source.
2. Exit the Windows connector when switching Car Thing to the phone.
3. Open **carthing-connector** and use **Open Bluetooth settings** to pair the device.
4. Select **Allow media access** and enable the connector's notification access. For a sideloaded app, Android may first require **Allow restricted settings** in the app's system information screen.
5. Select **Refresh paired devices**, grant Bluetooth access, and choose Car Thing.
6. Choose **Spotify** or **External media**, then select **Connect**. Spotify is the default.
7. Select the phone on Car Thing if necessary and play media on the phone.

Manual mode waits up to two minutes. With **Start after reboot** enabled, Connect uses continuous waiting and reconnection instead.

## Foreground service

- `connectedDevice` foreground service, started by the visible app's Connect action.
- One low-importance notification without sound or vibration, with an app shortcut and a **Disconnect** action.
- The setup screen can be closed while the connection service runs.
- App Disconnect and notification Disconnect stop the current service and remove its notification. Manual mode also stops after disconnection or failure; automatic mode retries transport failures.
- `NotSticky` restart policy: Android does not restart a killed service automatically. The boot receiver starts a new automatic session when enabled.
- Paired-device address validation on incoming RFCOMM connections.
- No notification contents read or stored by the media-access service.

On Android 13 and later, notification permission denial does not prevent starting the foreground service. Android can show the service in its active-apps interface instead of the notification drawer.

## Media adapter

| Function | Implementation |
|---|---|
| Session selection | Playing session first, then the previously selected active session, then the first active session |
| Track and playback state | Android `MediaController` metadata and playback state |
| Artwork | Metadata bitmaps, JPEG conversion up to 248 pixels and 60,000 bytes, shared image cache |
| Play/pause and previous/next | Transport controls gated by the selected session's advertised actions |
| Volume | Android music-stream volume and available volume steps |
| Display | Shared Spotify and External media protocol modes |
| Shuffle | Disabled in this adapter |

Transport calls are asynchronous. A successful RPC response means the command was dispatched, not that the player has confirmed the new state. State is read back from the media session. Album artwork depends on the player supplying a bitmap in its metadata.

## Source layout

| File | Responsibility |
|---|---|
| `android-connector/MainPage.cs` | Device picker, permissions, display mode, Connect and Disconnect |
| `Platforms/Android/ConnectorService.cs` | Foreground notification, RFCOMM listener, socket lifetime, protocol session |
| `Platforms/Android/AndroidMediaSource.cs` | Android implementation of `IMediaSource` |
| `Platforms/Android/MediaAccessService.cs` | Notification-listener binding and access check |
| `Platforms/Android/ConnectorPermissions.cs` | Version-specific runtime permission requests |

Files under `Platforms/Android/` are relative to `android-connector/`.

## Build and verification

See [Android development environment](59_Android_Development_Environment.md) for SDK setup.

```powershell
dotnet build android-connector/CarThing.Android.csproj -c Debug `
  "-p:AndroidSdkDirectory=$env:LOCALAPPDATA\Android\Sdk" `
  "-p:JavaSdkDirectory=$env:LOCALAPPDATA\Android\jdk"
```

Signed output: `android-connector/bin/Debug/net10.0-android/io.github.carthingconnector.android-Signed.apk`.

| Verification | Result |
|---|---|
| Debug APK build | 0 warnings, 0 errors |
| Shared core regression and stream-adapter tests | Passed |
| Merged manifest | `connectedDevice` type, Bluetooth permission, protected notification-listener binding |
| Packaged ARM64 core assembly | Present |
| APK signature verification | v2 and v3 signatures verified |

Logs: `analysis/android-connector-build.log`, `analysis/android-core-regression.log`, `analysis/android-connector-signature.log`.

Connection captures are stored in the app-private `files/captures/` directory. Debug package retrieval:

```powershell
adb shell run-as io.github.carthingconnector.android ls files/captures
```

Capture files contain media metadata and protocol messages. Device settings are stored in the app's private preferences; application backup is disabled.

References: [Android connected-device foreground services](https://developer.android.com/develop/background-work/services/fgs/service-types), [media session access](https://developer.android.com/reference/android/media/session/MediaSessionManager).


Display name: **Carthing Connector**. Executable names, package IDs, and existing settings paths remain unchanged.


## Start after reboot

Enable **Start after reboot** while disconnected, after granting media access and Bluetooth permission. The setting defaults to off. Select **Connect** to start automatic waiting immediately; subsequent boots start it after the phone is unlocked.

- `BOOT_COMPLETED` receiver with `RECEIVE_BOOT_COMPLETED` permission; no lock-screen or direct-boot startup.
- Automatic selection considers only paired devices whose name starts with `Car Thing`, ignoring case.
- A saved matching address takes priority. Otherwise, exactly one matching device is selected automatically.
- Multiple matches without a valid saved selection require selecting a device in the app. No arbitrary device selection.
- Bluetooth disabled, missing media access, no paired device, or ambiguous selection: keep the silent foreground notification and recheck every 10 seconds.
- A selected device is awaited with a blocking RFCOMM accept, without a two-minute timeout. Cancelling closes the listener.
- Transport loss: close the old connection and wait 10 seconds before listening again.
- **Disconnect** stops all current waiting and retries. The boot preference remains enabled for the next reboot; turn the switch off to disable future boot startup.
- Missing Bluetooth runtime permission prevents the boot receiver from starting the service. Android force-stop and restricted battery settings can also prevent boot delivery.

The setting changes future Connect/boot sessions; it does not replace a running manual connection. Disconnect first to change it. Pairing remains an Android system action; the app never pairs arbitrary nearby devices or enables Bluetooth itself.

Selection-policy tests cover no match, prefix filtering, case-insensitive matching, saved-address preference, multiple candidates, and stale saved addresses. Logs: `analysis/android-autostart-tests.log`, `analysis/android-autostart-build.log`.

## Volume step synchronization

- Captured Samsung Bluetooth output: five `volume_down` requests changed the internal fine index from 54 to 39 in increments of 3; reported public volume changed from 6 to 4 on a 0–15 range. All five requests reached the Android audio service.
- Relative commands now set an explicit public volume index, matching the `maximum + 1` levels advertised to Car Thing. Vendor-specific `AdjustStreamVolume` increments are no longer used.
- Pending targets accumulate consecutive commands. Older readback values are withheld until the target is observed or a 500 ms settling window expires; actual system state wins after expiry.
- Regression coverage: consecutive input with stale readback, direction reversal, confirmation, timeout, external changes, bounds, and range changes.
- Device validation (2026-10-27): updated APK installed on Samsung SM-F946N; user confirmed the volume issue was resolved with the Car Thing dial. Android Debug build completed with zero warnings and errors; automated regression tests passed.
- Evidence: `analysis/android-volume/capture-before.jsonl`, `audio-before.txt`, `tests.log`, and `build.log` in the same directory.

## Local Now Playing home card

The connector's local Home view now supplies one current-track card independently of Spotify library access. A recognized synthetic playlist context URI allows the stock player header to display the media app name. Home card selection accepts only the currently advertised track URI and maps it to resume; stale or unrelated URIs remain unsupported. Automated coverage: `tests/CarThing.Core.Tests/HomeCardTests.cs`; results: `analysis/home-card-tests.log`.

Device logs later showed repeated `com.spotify.superbird.get_home` calls rather than the GraphQL shelf request. Both routes now return the same current-track card; the legacy route includes the category URI required by the stock UI. Regression reproduced the unsupported legacy call before the fix and passed afterward. Evidence: `analysis/home-card-device.log`, `analysis/home-legacy-test-before.log`, and `analysis/home-legacy-test-after.log`. The user confirmed the player header ultimately displays Spotify after briefly showing its package identifier.

Device validation on 2026-10-27 with the Android Release build and stock Car Thing:

| Action | Observed result |
|---|---|
| Open the native player | App label changes from the initial package identifier to Spotify |
| Press Home | Now Playing section displays the current track |
| Select the current-track card | Returns to the playback screen |

Windows and Android share the home-card protocol implementation. Windows Release compilation and the common protocol regression tests passed; device observations above are from Android. The Android Release startup correction and app-label changes are described in [publishing](58_Publishing.md) and the [README](../README.md).

## Spotify library access probe

- Device test on 2026-10-27: Samsung SM-F946N, Spotify 9.1.92.3058, connector package `io.github.carthingconnector.android` (UID 10565).
- Enabled service: `com.spotify.mediabrowserservice.mediabrowserservice.SpotifyMediaBrowserService`; the separate Media3 library service was disabled for user 0.
- A temporary, debug-signed Android instrumentation APK targeted the installed connector and called the standard `MediaBrowser.connect()` API with its target context and null root hints.
- Result: `onConnectionFailed`; Spotify logged `No root for client io.github.carthingconnector.android`. No library root or child lists were returned. This establishes refusal for the tested client/version/request, not the absence of Spotify's browsing implementation.
- The preliminary `app_process` probe failed at service binding and was superseded by the instrumentation result.
- The temporary instrumentation APK and device-side probe file were removed after the test. The connector APK was unchanged.
- Local probe source and evidence: `analysis/media-browser-probe/BrowserProbe.java`, `AndroidManifest.xml`, `instrumentation-result.txt`, `service-result.txt`, and `spotify-package.txt`.
- API reference: [MediaBrowserService.onGetRoot](https://developer.android.com/reference/android/service/media/MediaBrowserService#onGetRoot(java.lang.String,int,android.os.Bundle)).
