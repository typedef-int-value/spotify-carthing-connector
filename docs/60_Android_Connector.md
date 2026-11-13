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
| Track and playback state | Android `MediaController` metadata and `MediaControllerCompat` playback state |
| Artwork | Metadata bitmaps, JPEG conversion up to 248 pixels and 60,000 bytes, shared image cache |
| Play/pause and previous/next | Transport controls gated by the selected session's advertised actions |
| Volume | Android music-stream volume and available volume steps |
| Display | Shared Spotify and External media protocol modes |
| Shuffle and repeat | Compat session readiness, mode state, and action checks |

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

## Android 재생 위치 탐색

- 미디어 세션이 `ACTION_SEEK_TO`를 지원하고 곡 길이가 0 ms보다 크면 `can_seek`를 활성화한다.
- `com.spotify.superbird.seek_to`의 `position`과 `com.spotify.set_playback_position`의 `position_ms`를 처리한다.
- 위치 단위는 ms이다. 요청 위치는 0부터 곡 길이까지 제한한다. 소수 ms는 버린다.
- 위치 누락, 숫자가 아닌 값, 무한대는 `wamp.error.invalid_argument`로 응답한다.
- 세션이 탐색을 지원하지 않거나 곡 길이가 없으면 명령을 실행하지 않고 `wamp.error.runtime_error`로 응답한다.
- Android `TransportControls.SeekTo()`로 요청한다. 실제 재생 위치는 기존 미디어 세션 조회 경로에서 다시 읽는다.
- 참고 UI 소스는 진행 바를 눌러 탐색 화면을 열고, 터치 또는 다이얼로 위치를 변경한다. 탐색 화면의 다이얼 이동 단위는 5,000 ms이다.

| 검증 항목 | 결과 |
|---|---|
| 두 요청 경로, ms 위치 전달 | 자동 시험 통과 |
| 음수, 곡 길이 초과, 소수 위치 처리 | 자동 시험 통과 |
| 잘못된 인자, 미지원 플레이어 오류 응답 | 자동 시험 통과 |
| Android ARM64 Release 증분 빌드 | 성공 |

시험 코드: `tests/CarThing.Core.Tests/SeekTests.cs`. 로그: `analysis/seek-tests.log`, `analysis/seek-android-build.log`.

## Android 1.0.1 미디어 호환 계층

- 앱 표시 버전을 `1.0.1`, 내부 버전 코드를 `2`로 변경한다. 앱 첫 화면에도 표시 버전을 출력한다.
- `MediaControllerCompat`를 선택된 Android 미디어 세션 토큰에 연결한다. 같은 세션에서는 호환 컨트롤러를 재사용한다.
- `Xamarin.AndroidX.Media 1.7.0.12`를 사용한다. MAUI 10.0.20의 AndroidX Core 1.16.0.3, Collection 1.5.0.3과 의존성을 맞춘다.
- 호환 계층의 준비 상태와 미디어 앱의 지원 플래그를 확인한다. 전송 후 상태는 미디어 세션에서 다시 읽는다.

| 기능 | 처리 조건과 동작 |
|---|---|
| 셔플 | 호환 세션 준비, 유효한 셔플 상태, 셔플 설정 지원 시 전달 |
| 반복 | 반복 설정 지원 시 끄기, 한 곡, 전체 반복 전달 |
| 상단 문구 | 앱 이름 표시. 큐 제목은 일반 재생 안내 문구일 수 있어 사용하지 않음 |
| 대기열 | 고유 항목 ID와 현재 항목 ID가 있으면 이전, 현재, 다음 항목으로 분리 |
| 대기열 선택 | 상대 인덱스를 Android 항목 ID로 변환, 세션이나 대기열이 바뀌면 기존 선택 거부 |
| 하트 평가 | 하트 평가와 평가 명령을 지원할 때 현재 곡에만 적용 |
| 배속 | 배속 명령 지원 시 0.5~3.5배 전달, 실제 배속 읽기와 이벤트 전송 |
| 팟캐스트 화면 | 메타데이터의 미디어 ID 또는 URI가 `spotify:episode:`일 때 선택 |

- 대기열의 현재 항목을 식별할 수 없으면 기존 현재 곡 표시를 사용한다. 현재 항목에는 재생 메타데이터의 커버를 연결한다. 이전, 다음 항목 이미지는 빈값으로 보낸다.
- 하트 평가는 미디어 앱의 평가 기능이다. Spotify 라이브러리 저장과 동일하다고 가정하지 않는다.
- 자동 시험에서 요청 분기, 잘못된 인자, 상대 큐 인덱스, 큐 변경 후 선택 거부, 배속 상태와 내부 필드 분리를 확인했다.
- 시험 코드: `tests/CarThing.Core.Tests/CompatMediaTests.cs`. 결과: `analysis/compat-tests.log`.

## 호환 계층 적용 후 커버 표시 수정

- 사용자 연동 시험에서 Android 셔플, Seek 동작을 확인했다.
- 앱 제공 대기열의 현재 항목에 이미지 ID가 없어 순정 재생 화면의 커버가 사라졌다. 커버가 있는 재생 상태와 이미지가 없는 대기열을 함께 보내는 자동 시험에서 재현했다.
- 순정 UI의 `Artwork.tsx`는 현재 대기열 항목의 `image_uri`를 사용한다. 공통 브리지에서 현재 재생 메타데이터의 커버 ID를 해당 필드에 연결했다.
- 커버 없음, 지연 도착, 변경 시 대기열 커버가 함께 갱신되는지 확인했다. 이전, 다음 항목에는 현재 곡 커버를 복사하지 않는다.
- 상단 문구를 큐 제목에서 앱 이름으로 복원했다. 사용자가 본 ‘재생 목록 재생하기’와 같은 일반 큐 문구를 플레이리스트 이름으로 취급하지 않는다.
- 재현 로그: `analysis/compat-cover-before.log`. 수정 후 자동 시험: `analysis/compat-cover-after.log`. Android 빌드: `analysis/compat-cover-build.log`.

## Android 1.0.1 사용자 연동 결과

- Spotify 재생, 순정 Car Thing, 커버 수정 APK 기준 사용자 확인 결과이다.
- 반복과 하트의 원인은 세션 지원 정보 또는 명령 처리 여부로 확정하지 않는다.

| 기능 | 확인 결과 |
|---|---|
| 셔플, Seek | 정상 동작 |
| 앨범 커버, 상단 앱 이름 | 수정 후 정상 표시 |
| 반복, 하트 | 동작하지 않는 것으로 관찰 |
| 재생 대기열 | 현재 곡만 표시 |
