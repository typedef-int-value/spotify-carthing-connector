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
- 재생·일시정지·다음·이전 곡 요청을 Windows 현재 미디어 세션에 전달
- 다음·이전 곡 지원 여부를 현재 앱의 Windows 미디어 제어 정보에 맞춰 전달
- 볼륨 RPC를 Windows 기본 멀티미디어 출력 장치의 볼륨 제어에 연결

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

## 연속 재생 중 커버 이미지 누락 수정

- 2026-10-20 장시간 재생 중 이미지 요청 실패 확인
- 원인: `Dictionary`에서 삭제된 자리를 새 항목이 재사용하면서, 첫 항목 제거 시 현재 이미지 삭제
- 수정: 삽입 순서를 유지하는 `OrderedDictionary`로 최근 이미지 8개 보관
- 동일 이미지의 주기적 재수집 및 `com.spotify.get_image` 응답 경로를 포함한 회귀 시험 추가

| 확인 항목 | 결과 |
|---|---|
| 기존 실기 로그 | `Safe Passage`부터 이미지 요청에 `wamp.error.no_such_procedure` 응답 |
| 기존 코드 재현 | 10번째 이미지의 첫 조회 실패 |
| 수정 후 회귀 시험 | 24곡 × 곡당 3회 조회, 72회 정상 응답 |
| 캐시 보관 검사 | 최근 8개 유지·이전 이미지 제거 통과 |
| 기존 프로토콜 시험 | 전체 통과 |
| 배포 실행 파일 | 수정본 반영 및 브리지 재연결 |
| 재연결 후 실기 확인 | 다음 곡 전환 후 사용자 커버 표시 확인, 248×248 pixels·Base64 15,708 characters 응답 |

- [수정 전 재현 로그](../analysis/image-cache-before.log), [수정 후 회귀 시험](../analysis/image-cache-after.log)
- [수정본 배포 로그](../analysis/image-cache-publish.log), [재연결 로그](../analysis/image-cache-runtime-fixed.log)
- [수정 후 실기 캡처](../analysis/captures/20261020-061349-223/)

## 실행

- 현재 기본 실행: `dist/carthing-connector/carthing-connector.exe` 더블클릭
- [트레이 앱 사용·배포](54_carthing-connector_트레이_앱.md), 아래 명령은 진단용 직접 실행 방식

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
| `--volume` | 기본 출력 장치의 볼륨·단계 수 JSON 출력 |
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
  --self-contained true -p:PublishSingleFile=true -o dist\carthing-connector
```

- 환경: .NET SDK 10.0.401, Windows 10 build 19041 이상 대상, MessagePack 3.1.12
- 볼륨 제어 의존성: NAudio.Wasapi 2.2.1

## 버튼 제어 구현

- 2026-10-20 제어 요청 처리 및 배포 실행 파일 갱신
- 재생 제어 대상: Windows 현재 미디어 세션, 볼륨 대상: Windows 기본 멀티미디어 출력 장치
- Windows 요청 수락 시 RESULT, 제어 거절·장치 오류 시 RPC 오류 응답

| 항목 | 확인 결과 |
|---|---|
| 초기 버튼 시험 | `com.spotify.superbird.pause` 수신 |
| 홈·프리셋 관련 화면 | `com.spotify.superbird.graphql`의 shelf·presets 조회 기록 |
| 제어 경로 시험 | 재생·일시정지·다음·이전·볼륨 증가·감소·설정 7개 경로 통과 |
| 오류 시험 | Windows 요청 거절·예외·잘못된 볼륨 값 처리 통과 |
| 볼륨 이벤트 시험 | 구독·중복 억제·구독 해제 검사 통과 |
| 기존 회귀 시험 | 이미지 24곡·72회 및 프로토콜 시험 통과 |
| 증분 빌드 | 경고 0개·오류 0개 |
| 기본 출력 장치 조회 | 볼륨 단계 수 51, `--volume` 조회 성공 |
| 사용자 실기 확인 | 이전 곡·다음 곡·일시정지·볼륨 증가·감소 정상 동작 |
| 제어 요청 수락 기록 | 재생 1회·일시정지 1회·다음 곡 1회·이전 곡 2회·볼륨 증가 45회·감소 53회 |
| 일시정지 연속 요청 | 2회 중 1회 수락, 1회 Windows 반환값 false에 따른 RPC 오류 |

- 상단 4개 버튼의 기본 용도는 프리셋, 현재 기록은 원시 버튼 번호가 아닌 라이브러리 조회 요청
- 위 실기 집계의 캡처: `analysis/captures/20261020-063744-415/`, 2026-10-20 15:53~15:54 KST 버튼 시험
- [빌드](../analysis/control-build.log), [자동 시험](../analysis/control-self-test.log), [실행](../analysis/control-runtime-final.log), [실행 오류](../analysis/control-runtime-final-error.log)

## 프리셋·홈 버튼 입력 관찰

- 조건: 2026-10-20 15:59~16:02 KST, 현재 브리지의 외부 미디어 재생 화면, 버튼별 짧게 누르기
- 버튼별 시험 시작 시각과 수신 구간 분리, 사용자 화면 확인 및 RFCOMM 로그 대조

| 입력 | 화면 변화 | 수신 결과 |
|---|---|---|
| 프리셋 1번 1회 및 추가 4회 | 없음 | 해당 구간 새 메시지 0개 |
| 프리셋 2번 1회 | 없음 | 야간 모드 진단 로그 1건, 버튼 제어 요청 없음 |
| 프리셋 3번 1회 | 없음 | 해당 구간 새 메시지 0개 |
| 프리셋 4번 1회 | 없음 | 야간 모드 진단 로그 1건, 버튼 제어 요청 없음 |
| 홈/뒤로 1회 | 검은 화면 | `com.spotify.superbird.graphql`의 `shelf` 조회 3회 |

- 홈 조회: `shelf(limit:14 overrides:[])`, 응답 요청 필드 `title`·`id`·`total`·`children{uri title subtitle image_id:imageId}`
- 홈 조회 요청 ID `1474`, `1478`, `1486`에 `wamp.error.no_such_procedure` 응답
- 기기 오류 보고의 `query home failed: Interapp error: wamp.error.no_such_procedure` 확인
- 사용자 확인: 잠시 후 재생 화면 자동 복귀, 홈/뒤로 버튼 재입력 시 검은 화면 재발
- 홈 버튼 자체의 원시 입력 대신 홈 화면 콘텐츠 조회 요청 수신
- 프리셋 결론의 적용 조건: 현재 재생 화면·현재 프리셋 상태의 짧은 입력, 다른 화면이나 프리셋 등록 후 동작과 구분
- 원본 구간 및 시험 시각: [버튼 입력 기록](../analysis/button-input-check/)

## Play Spotify 터치 버튼 관찰

- 조건: 2026-10-20 16:43~16:45 KST, 외부 미디어 재생 화면 상단의 `Play Spotify` 입력
- 사용자 확인: 아이콘 깜빡임, 화면 변화 없음
- 로그에서 아래 요청 순서 3회 확인

| 순서 | 요청 | 인자 | 현재 응답 |
|---|---|---|---|
| 1 | `com.spotify.superbird.set_active_app` | `{"app":"spotify"}` | `wamp.error.no_such_procedure` |
| 2 | `com.spotify.superbird.resume` | `{}` | `wamp.error.runtime_error` |

- 요청 ID 쌍: `2066/2067`, `2076/2077`, `2090/2091`
- 버튼 역할: Spotify 활성 앱 전환 요청 후 재생 재개 요청
- 현재 브리지의 앱 전환 기능 미지원 확인, 이 응답만으로 순정 화면 전환 조건을 단정하지 않는 관찰 결과
- 원본 요청·응답: [Play Spotify 입력 기록](../analysis/button-input-check/play-spotify.jsonl)
