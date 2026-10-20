# Car Thing APK 분석 및 Windows 미디어 연동

## 구현 결과

- 2026-10-20 순정 Car Thing과 Windows 공통 미디어 세션 연동
- Windows `GlobalSystemMediaTransportControlsSessionManager`의 현재 세션에서 앱 이름·곡명·아티스트·앨범·재생 상태·재생 위치 수집
- Bluetooth RFCOMM 서버, WAMP HELLO·CHALLENGE·AUTHENTICATE·WELCOME 및 구독 처리
- `com.spotify.superbird.player_state`로 미디어 변경 전달
- `com.spotify.get_image`로 최대 248×248 JPEG·Base64 응답, 최근 이미지 8개 보관
- 앱 표시 이름 조회를 통한 긴 Windows 앱 식별자 표시 수정
- 수신 진단 로그의 로컬 저장 및 접수 응답
- 로컬 브리지 연결 상태를 `com.spotify.session_state`로 전달, Spotify 클라우드 로그인과 구분
- 표시 전용 구현, 재생 제어 가능 여부를 false로 전달

## 시험 결과

- 조건: 순정 Car Thing `Q61S`, 소프트웨어 `8.4.4-release`, Windows의 Spotify PC 앱 재생
- Bluetooth 주소: `30E3D604D71F`
- 사용자 확인: 제목·커버 이미지·곡 변경 표시 정상

| 항목 | 결과 |
|---|---|
| 초기 수신 시험 | 접속 3회, 회당 477 bytes |
| 초기 프레임 | 길이 4 bytes + MessagePack HELLO 473 bytes |
| 초기 데이터 비교 | 3회 SHA-256 동일 |
| 선택 서비스 UUID | `E3CCCCCD-33B7-457D-A03C-AA1C54BF617F` |
| WAMP 초기 절차 | HELLO → CHALLENGE → AUTHENTICATE → WELCOME |
| 최종 실행의 구독 | 14개 수신·응답 |
| 세션 상태 응답 전 화면 | 사용자 보고 `Not logged in` |
| 세션 상태 응답 후 화면 | 미디어 화면 전환 확인 |
| 앱 이름 | `Spotify` |
| 곡 변경 기록 | `Night Mirror / Airhaven` → `Dreams Within / Silence Concertos` |
| 이미지 응답 확인 | 248×248 pixels, Base64 6,888 characters |
| 프로토콜 회귀 시험 | 인증 순서·구독 ID·세션 상태·RPC 오류·구독 해제·분할 수신·초과 길이·잘린 프레임 통과 |
| 배포 | Windows x64, .NET 런타임 포함 실행 파일 생성 |

- 최종 실행 로그의 2026-10-20 05:38 UTC 집계: WELCOME 1개, SUBSCRIBED 14개, EVENT 32개
- 지원하지 않는 OTA·원격 등록·원격 설정 요청에 오류 응답 3개 기록
- 실기기 표시 확인 조건: Windows 공통 API 경유 Spotify PC 앱의 정보 전달

## 실행

```powershell
Set-Location D:\git\Spotify
.\Start-CarThing.ps1
```

- 기본 대상: 현재 페어링된 Car Thing `30E3D604D71F`
- 기본 실행 시간: Ctrl+C 종료까지 유지
- 이미 브리지 실행 중인 경우 동일 서비스를 사용하는 추가 실행 방지 필요
- 다른 기기 지정: `Start-CarThing.ps1 -DeviceAddress AA:BB:CC:DD:EE:FF`
- Windows 미디어 API에 정보를 게시하는 앱의 현재 세션 선택

| 옵션 | 용도 |
|---|---|
| `--list` | 페어링 장치 조회 |
| `--media` | 현재 Windows 미디어 정보 JSON 출력 |
| `--bridge <MAC> [초]` | 연결 응답 및 미디어 전달, 시간 지정 시 1~3600초 |
| `--listen <MAC> [초]` | 수신 전용 진단, 기본 120초 |
| `--check-radio` | 3초간 서비스 광고 확인 |
| `--self-test` | 프로토콜 회귀 시험 |

- 실행 파일: [CarThingProbe.exe](../dist/CarThingProbe/CarThingProbe.exe)
- 실행 스크립트: [Start-CarThing.ps1](../Start-CarThing.ps1)
- 소스: [Program.cs](../windows-probe/Program.cs), [MediaBridge.cs](../windows-probe/MediaBridge.cs), [WindowsMedia.cs](../windows-probe/WindowsMedia.cs)
- 캡처 저장 위치: 작업 폴더의 `analysis/captures/<UTC 시각>/`
- `.rx.bin`·`.tx.bin`: 길이 헤더를 포함한 수신·송신 프레임
- `.jsonl`: UTC 시각·방향·메시지 JSON을 기록한 줄 단위 로그

## 근거 파일

- [초기 HELLO 수신·해석](../analysis/captures/20261020-052508-434/summary.json)
- [이미지 응답 시험](../analysis/captures/20261020-053610-651/)
- [최종 실행 집계 및 곡 변경 기록](../analysis/captures/20261020-053750-823/verification-summary.json)
- [프로토콜 회귀 시험](../analysis/bridge-self-test.log)
- [배포 로그](../analysis/bridge-publish.log)
- [실행 파일 SHA-256](../analysis/probe-sha256.txt)

## APK 분석 근거

- 대상: `Spotify_ Music and Podcasts_9.0.10.415_APKPure - CarThings.apk`, 109,245,996 bytes
- 원본 보관 위치: `artifacts/apk/`, [저장소 구성](51_저장소_구성.md) 참조
- SHA-256: `3196d9a56f38f5ff2b81b23991cae87000e75fe097aa8e3c2cd4c4d78f078fd5`
- JADX 1.5.6 추출 결과: 전체 처리 오류 395건, 확인 가능한 메서드와 실기기 패킷 대조
- Superbird URI 문자열 47종, 호출 등록 코드의 URI 38종: [추출 목록](../analysis/superbird-endpoints.json)
- 디컴파일 소스 위치: `analysis/jadx/sources/`

| 프로토콜 항목 | APK 소스 |
|---|---|
| 서버 UUID·길이 프레임 | `p/xw3.java`, `p/toy0.java`, `p/soy0.java` |
| CHALLENGE·AUTHENTICATE 처리 | `p/beo.java`, `p/ef51.java`, `p/ze51.java` |
| WELCOME·구독 응답 | `p/ymu.java` |
| EVENT의 payload 위치 | `p/mfm.java` |
| 다른 앱의 미디어 변환 | `p/cpn.java`의 `E(sm80)` |
| 플레이어 상태 토픽 | `p/w1z0.java` |
| 세션 상태 | `com/spotify/interapp/model/AppProtocol$SessionState.java` |
| 이미지 응답 | `com/spotify/interapp/model/AppProtocol$Image.java`, `p/pf10.java` |

- APK의 다른 앱 경로에 사용된 `spotify:*:fake` URI 유지, 실제 Spotify 카탈로그 조회 없이 미디어 표시
- [Windows 미디어 세션 API](https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssessionmanager)
- [Windows 앱 표시 이름 API](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.appinfo.getfromappusermodelid)

## 빌드

```powershell
dotnet publish windows-probe\CarThingProbe.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true -o dist\CarThingProbe
```

- 환경: .NET SDK 10.0.401, Windows 10 build 19041 이상 대상, MessagePack 3.1.12
