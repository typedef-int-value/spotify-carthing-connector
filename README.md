# carthing-connector

Use a stock Spotify Car Thing as a display and controller for Windows media playback.

**carthing-connector** is a Windows tray app that sends track information and album artwork to Car Thing over Bluetooth and routes playback controls back to the PC. It uses Windows media sessions, so the integration is designed for compatible media apps rather than a Spotify account API.

![Stock Car Thing displaying the track playing in Spotify on a Windows PC](docs/images/pc-carthing-stock-mode.jpg)

*Spotify on Windows and a stock Car Thing showing the same track in the device's native Spotify playback view.*

## Features

- Track title, artist, album artwork, and playback-state updates.
- Play/pause, previous track, and next track controls.
- PC output volume control using the Car Thing dial.
- Shuffle control and state synchronization when supported by the active media app.
- Two display modes: **External media** and **Spotify**.
- Tray-based device selection, connection restart, and log access.
- Saved device and display-mode settings.
- A self-contained Windows x64 executable; no separate .NET installation or PowerShell launcher needed.

Playback, artwork updates, volume, and bidirectional shuffle have been verified with the Spotify Windows app. Other apps must expose a Windows media session, and their available controls may differ.

## Display modes

| Mode | Car Thing display |
|---|---|
| External media | The device's external-media card, including the playback app name |
| Spotify | The device's native Spotify playback view, with artwork and playback controls |

Both modes use the active **Windows media session** as their source. Choosing Spotify mode changes the device UI; it does not connect the bridge to a Spotify account or turn local track identifiers into Spotify catalog IDs.

Select a mode from the tray menu. The app closes the current connection, starts listening again, and remembers the choice for the next launch.

## Getting started

You need a Windows x64 PC with Bluetooth Classic support and a stock Car Thing with its companion UI available.

1. Power on Car Thing and pair it with the PC in Windows Bluetooth settings.
2. Build the executable using the command below.
3. Run `dist/carthing-connector/carthing-connector.exe`.
4. Select the paired Car Thing when prompted. The app remembers this device.
5. Start playback in a compatible Windows media app. If Car Thing remains on its connection screen, select the PC on the device.
6. Right-click the tray icon to select a display mode, restart the connection, open logs, or exit.

The current tray UI uses Korean labels:

| Tray label | Meaning |
|---|---|
| 장치 선택… | Select device |
| 화면 모드 → 외부 미디어 | Display mode → External media |
| 화면 모드 → Spotify | Display mode → Spotify |
| 연결 다시 시작 | Restart connection |
| 로그 폴더 열기 | Open logs folder |
| 종료 | Exit |

Only one connector instance runs per Windows session. Exit the running app before replacing its executable.

## Build and test

Install the .NET 10 SDK on Windows, then run these commands from the repository root.

Build the Windows app:

```powershell
dotnet build windows-probe/CarThingProbe.csproj -c Release
```

Create a self-contained, single-file Windows x64 distribution:

```powershell
dotnet publish windows-probe/CarThingProbe.csproj -c Release -r win-x64 --self-contained true -o dist/carthing-connector
```

Copy `carthing-connector.exe` from that directory to the destination PC. The runtime and dependencies are bundled; native components may be extracted automatically at launch.

Run the platform-independent protocol and adapter tests:

```powershell
dotnet run --project tests/CarThing.Core.Tests/CarThing.Core.Tests.csproj -c Release
```

These tests cover authentication, subscriptions, RPC handling, image caching, playback commands, both display modes, and stream-session integration using a fake media source. They do not require a connected Car Thing.

## Architecture

```text
Car Thing
    ↕ Bluetooth Classic RFCOMM
Windows Bluetooth adapter
    ↕ streams
CarThing.Core
    ↕ IMediaSource
Windows media sessions and output volume
```

| Project | Responsibility |
|---|---|
| `carthing-core/` | Platform-independent .NET 10 library: WAMP messages, MessagePack framing, display modes, command helpers, and image cache |
| `windows-probe/` | Windows tray app, Bluetooth connection, media-session integration, artwork conversion, and output volume |
| `tests/CarThing.Core.Tests/` | Protocol regression tests and stream/adapter integration tests |

The shared library accepts ordinary .NET streams and an `IMediaSource` implementation. An Android app can reuse this core by supplying its own Bluetooth transport, media-session adapter, and image conversion. An Android application is not included yet; MAUI is an optional UI framework rather than a dependency of the core.

## Current scope

- **Presets:** listing, save requests, and selection requests have been explored. Spotify mode currently uses session-local test presets; they are not mapped to real playlists or Windows actions.
- **Home/browse:** incomplete. The Home button can lead to an empty or black screen.
- **Likes, seeking, and repeat:** not implemented.
- **Authentication:** the bridge implements the paired companion exchange used by the device, not Spotify cloud login.

## Settings and diagnostics

Settings and logs are stored independently of the executable location:

```text
%LocalAppData%/carthing-connector/
├── settings.json
└── logs/
    ├── app-*.log
    └── captures/
```

Settings include the selected Bluetooth device and display mode. Captures contain raw received/transmitted frames and decoded message logs for protocol investigation.

## Protocol notes

The implementation uses Bluetooth Classic RFCOMM, length-prefixed MessagePack frames, and WAMP-style messages. Research notes include APK analysis and physical-device observations.

The detailed documents are currently in Korean:

- [Protocol specification](docs/52_CarThing_연동_프로토콜_사양.md)
- [Shared library and platform adapter contract](docs/57_공통_프로토콜_라이브러리.md)
- [Windows tray app and deployment](docs/54_carthing-connector_트레이_앱.md)
- [Native Spotify UI, presets, and artwork experiments](docs/55_Spotify_화면_전환_시험.md)
- [Older APK API comparison](docs/56_구버전_APK_API_비교.md)
- [Initial APK analysis and Windows integration](docs/CarThing_APK_분석_및_Windows_연결.md)
- [Repository layout](docs/51_저장소_구성.md)

This is an independent project and is not affiliated with or endorsed by Spotify.
