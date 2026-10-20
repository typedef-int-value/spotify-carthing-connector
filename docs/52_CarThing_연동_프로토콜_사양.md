# Car Thing 미디어 표시 연동 프로토콜

- 문서 버전: 0.2, 2026-10-20
- 목적: Android·Windows 등에서 순정 Car Thing에 현재 미디어 정보와 커버 이미지를 전달하는 구현 기준
- 성격: APK 분석과 실기 패킷으로 정리한 호환 구현 사양, 제조사 공식 사양과 구분
- 기준 구현: Git `eea6e60`의 표시 구현 및 이후 제어 작업본, [연결부](../windows-probe/Program.cs), [프로토콜](../windows-probe/MediaBridge.cs), [미디어·이미지](../windows-probe/WindowsMedia.cs), [볼륨](../windows-probe/WindowsVolume.cs)

## 1. 근거와 적용 조건

| 항목 | 근거·조건 |
|---|---|
| 실기 확인 | Car Thing Q61S, 소프트웨어 `8.4.4-release`, 앱 `0.42.3` |
| 표시 확인 | Windows Spotify PC 앱 → Windows 미디어 세션 → Bluetooth → Car Thing |
| 분석 APK | Spotify Android `9.0.10.415`, SHA-256 및 분석 경로는 [분석 문서](CarThing_APK_분석_및_Windows_연결.md) 참조 |
| 패킷·표시 검증 | 세션 연결, 제목·아티스트·커버, 곡 변경, 이미지 요청·응답 |
| 구현 정책 | 이미지 248×248 이하, JPEG 60,000 bytes 이하, 최근 이미지 8개 보관 |
| 이식 설계 | 공통 프로토콜과 OS별 연결·미디어 수집 분리, Android API는 공식 문서에 따른 대응 후보 |

- 이미지 크기·보관 개수·갱신 주기는 현재 브리지의 정책이며 기기 하드웨어 한계와 구분
- 미디어 표시 및 현재 Windows 세션의 재생·일시정지·다음·이전 곡 제어 프로파일

## 2. 연결 역할과 전송

| 항목 | 값·동작 |
|---|---|
| 전송 계층 | Bluetooth Classic RFCOMM 바이트 스트림 |
| 연결 수신 측 | Windows·Android의 동반 앱 |
| 연결 시작 측 | Car Thing |
| 서비스 UUID | `E3CCCCCD-33B7-457D-A03C-AA1C54BF617F` |
| SDP 서비스 이름 | `Spotify`, 속성 `0x0100` |
| Windows SDP 값 | `25 07 53 70 6F 74 69 66 79` |
| 연결 조건 | OS 페어링 완료, 인증·암호화 연결, 수신 장치 주소 대조 |
| 프레임 | 4-byte big-endian 길이 + MessagePack 메시지 |
| 길이 의미 | 헤더를 제외한 MessagePack 바이트 수 |
| 현재 길이 검사 | `1 <= N <= 120000` |
| 별도 UUID | `7FBE865E-518B-462A-B31B-90ACF6A472FC`, APK 분석의 escaped/CRC 경로; 표시 구현은 위 길이 프레임 경로 사용 |

```text
frame = int32_big_endian(payload.length) + MessagePack.encode(message)

반복:
    header = read_exactly(4)
    N = decode_big_endian(header)
    1 <= N <= 120000 검사
    payload = read_exactly(N)
    message = MessagePack.decode(payload)
    상태별 메시지 처리
```

- RFCOMM 1회 읽기와 메시지 1개의 경계 구분, 분할 수신·연속 프레임 처리
- 아래 JSON 예제는 MessagePack 자료형을 읽기 쉽게 표현한 표기
- 전송 시 배열·문자열·정수·실수·불리언·null·맵 타입 유지, JSON 문자열 자체의 전송 금지
- 빈 맵 `{}`과 빈 배열 `[]` 구분
- 송신 프레임 및 세션 상태 변경의 직렬 처리
- 연결 종료 시 해당 연결의 구독·인증 단계 초기화

## 3. 초기 연결 절차

| 순서 | 방향 | 메시지 |
|---|---|---|
| 1 | 기기 → 앱 | HELLO `[1, "spotify", details]` |
| 2 | 앱 → 기기 | CHALLENGE `[4, "wampcra", {"challenge": string}]` |
| 3 | 기기 → 앱 | AUTHENTICATE `[5, signature, details]` |
| 4 | 앱 → 기기 | WELCOME `[2, session_id, details]` |
| 5 | 기기 → 앱 | SUBSCRIBE 또는 CALL |

- HELLO의 realm `spotify`, `authmethods`에 `wampcra` 포함 여부 확인
- HELLO `details.authid`를 challenge 내부의 `authid`에 반영
- `challenge` 값은 맵이 아닌 JSON 문자열

```json
[4, "wampcra", {
  "challenge": "{\"nonce\":\"dummy_nounce\",\"authid\":\"example\",\"timestamp\":\"2026-10-20T06:00:00.000Z\",\"authmethod\":\"wampcra\"}"
}]
```

- 위 `dummy_nounce`는 현재 구현에서 사용하는 리터럴
- 현재 구현은 AUTHENTICATE 서명의 존재만 검사하며 암호학적 서명 검증은 수행하지 않는 로컬 호환 동작
- 신뢰 경계는 페어링·암호화·대상 장치 검사, 이 절차의 성공은 Spotify 계정 인증을 의미하지 않음
- 초기 단계 이외의 AUTHENTICATE 등 순서 위반은 연결 오류 처리

```json
[2, 1, {
  "roles": {"broker": {}, "dealer": {}},
  "app_version": "9.0.10.415",
  "authprovider": "",
  "authid": "",
  "authrole": "",
  "authmethod": "",
  "date_time": "2026-10-20T06:00:00.000Z"
}]
```

- `session_id=1` 및 위 빈 인증 필드는 현재 구현의 값
- 날짜 필드는 송신 시각으로 생성

## 4. 구독과 이벤트

| 메시지 | 배열 형식 |
|---|---|
| SUBSCRIBE | `[32, request_id, {}, topic]` |
| SUBSCRIBED | `[33, request_id, subscription_id]` |
| EVENT | `[36, subscription_id, publication_id, {}, [], payload_map]` |
| UNSUBSCRIBE | `[34, request_id, subscription_id]` |
| UNSUBSCRIBED | `[35, request_id]` |

- `request_id`는 수신값 그대로 응답, `subscription_id`는 연결 내 증가값 할당
- EVENT에는 요청 ID가 아닌 해당 토픽의 구독 ID 사용
- `publication_id`는 이벤트마다 증가, 실제 payload는 배열 인덱스 5
- 구독 해제 후 해당 구독으로 이벤트 송신 중지
- 새 구독 처리 후 현재 미디어 상태 재전송

| 토픽 | 현재 처리 |
|---|---|
| `com.spotify.session_state` | 구독 직후 아래 세션 상태 전달 |
| `com.spotify.superbird.player_state` | 현재 미디어 전달 및 변경 시 갱신 |
| `com.spotify.superbird.volume.volume_state` | 구독이 있는 연결에 기본 출력 장치의 볼륨 변경 전달 |
| 그 외 토픽 | 구독 ID 발급, 상태 발행은 구현한 토픽에 한정 |

```json
{
  "is_logged_in": true,
  "is_offline": false,
  "is_in_forced_offline_mode": false,
  "connection_type": "unknown"
}
```

- 위 세션 상태 전달 전 `Not logged in`, 전달 후 미디어 화면 전환 확인
- `is_logged_in`은 이 구현의 로컬 동반 앱 세션 상태

## 5. 미디어 상태

- 아래 객체 전체를 `com.spotify.superbird.player_state` EVENT의 payload로 사용
- 필드의 필수 여부를 개별 제거 시험한 스키마가 아닌, 표시 성공을 확인한 기준 객체

```json
{
  "currently_active_application": {"id": "example.player", "name": "Example Player"},
  "context_uri": "spotify:context:fake",
  "context_title": "Example Album",
  "is_paused": false,
  "is_paused_bool": false,
  "playback_options": {"repeat": 0, "shuffle": false},
  "playback_position": 12000,
  "playback_speed": 1,
  "playback_restrictions": {
    "can_repeat_context": false,
    "can_repeat_track": false,
    "can_seek": false,
    "can_skip_next": false,
    "can_skip_prev": false,
    "can_toggle_shuffle": false
  },
  "track": {
    "album": {"name": "Example Album", "uri": "spotify:album:fake"},
    "artist": {"name": "Example Artist", "uri": "spotify:artist:fake"},
    "artists": [{"name": "Example Artist", "uri": "spotify:artist:fake"}],
    "duration_ms": 180000,
    "image_id": "example-image-id",
    "image_bytes": null,
    "is_episode": false,
    "is_podcast": false,
    "name": "Example Track",
    "saved": false,
    "uid": "fake",
    "uri": "spotify:track:fake"
  }
}
```

| 필드 | 기준 구현의 값·단위 |
|---|---|
| `currently_active_application.id` | 미디어 제공 앱 식별자 |
| `currently_active_application.name` | 화면 표시용 앱 이름, 긴 패키지 ID와 구분 |
| `playback_position`, `track.duration_ms` | 밀리초 정수 |
| `playback_speed` | 재생 시 재생 배율, 정지 시 0 |
| `is_paused`, `is_paused_bool` | 같은 재생 상태를 두 필드에 반영 |
| `track.image_id` | 캐시에 저장된 이미지 식별자, 이미지가 없으면 빈 문자열 |
| `track.image_bytes` | null, 이미지는 별도 RPC로 전달 |
| `spotify:*:fake`, `uid` | APK의 다른 앱 표시 경로를 따른 기준 구현 값 |

- 기준 구현: 약 1초 간격 또는 요청 처리 후 미디어 조회, 직전 객체와 동일하면 전송 생략
- 이미지 조회: 곡 식별 정보 변경 시 또는 5초 간격
- 위 시간 간격은 브리지 정책이며 프로토콜 고정 타이머와 구분
- 제어 구현에서는 예제의 `can_skip_next`·`can_skip_prev`를 현재 Windows 세션의 지원 여부로 교체

## 6. RPC와 이미지

| 메시지 | 배열 형식 |
|---|---|
| CALL | `[48, request_id, {}, procedure, [], kwargs_map]` |
| RESULT | `[50, request_id, {}, result_map]` |
| ERROR | `[8, 48, request_id, {}, error_uri]` |

- RESULT의 결과는 배열 인덱스 3의 **맵**, 위치 인자 배열로 한 번 더 감싸지 않는 현재 APK·실기 호환 형식
- 범용 WAMP 라이브러리 도입 시 위 EVENT·RESULT 배열 배치 확인 필요

```json
[48, 101, {}, "com.spotify.get_image", [], {"id": "example-image-id"}]
```

```json
[50, 101, {}, {"image_data": "<JPEG bytes encoded as Base64>", "width": 248, "height": 248}]
```

- `image_data`는 JPEG 바이트의 Base64 문자열, data URI 접두어 없이 전달
- 가로·세로 비율 유지, 긴 변 248 pixels 이하, 원본보다 확대하지 않는 기준 구현
- `width`, `height`는 변환된 실제 크기
- JPEG 원본 바이트 60,000 이하일 때 캐시에 저장, Base64 및 메시지 부가 데이터를 포함한 최종 프레임 길이 별도 검사
- 식별자: 변환된 JPEG의 SHA-256 소문자 64자리 16진수 사용
- **캐시 저장 완료 → image_id 이벤트 발행 → get_image 응답** 순서 유지
- 삽입 순서로 최근 고유 이미지 8개 보관, 동일 이미지 재저장은 항목 추가 없이 갱신
- 캐시 적중 응답은 저장 객체의 복사본 사용
- 기존 캐시 오류: 해시 테이블의 열거 첫 항목을 제거하다 현재 이미지 삭제, 10번째 이미지에서 재현

| procedure | 기준 응답 |
|---|---|
| `com.spotify.get_image` | 해당 ID의 이미지 객체 |
| `com.spotify.superbird.permissions` | `{"can_use_superbird": true}` |
| `com.spotify.get_children_of_item` | 요청 `limit`·`offset` 반영, `total:0`, `items:[]` |
| `com.spotify.superbird.pitstop.log` | 요청 기록 후 `{}` |
| `com.spotify.superbird.crashes.report` | 요청 기록 후 `{}` |
| `com.spotify.superbird.instrumentation.log` | 요청 기록 후 `{}` |
| `com.spotify.superbird.instrumentation.request` | 요청 기록 후 `{}` |
| `com.spotify.superbird.instrumentation.impression` | 요청 기록 후 `{}` |
| `com.spotify.superbird.instrumentation.interaction` | 요청 기록 후 `{}` |
| 그 외 호출 | `wamp.error.no_such_procedure` |

- 현재 이미지 조회 실패도 `wamp.error.no_such_procedure`로 응답하는 구현 특성
- 이미지 오류 URI 자체를 제조사 규약으로 해석하지 않고, 요청 ID와 캐시 상태를 함께 진단
- GOODBYE 수신 `[6, details, reason]` → `[6, {}, "wamp.close.goodbye_and_out"]` 응답 후 연결 종료

### 제어 RPC

| procedure | 인자 | Windows 연결 |
|---|---|---|
| `com.spotify.superbird.resume` | `{}` | `TryPlayAsync` |
| `com.spotify.superbird.pause` | `{}` | `TryPauseAsync` |
| `com.spotify.superbird.skip_next` | `{}` | `TrySkipNextAsync` |
| `com.spotify.superbird.skip_prev` | `{}` 또는 `allow_seeking` 포함 | `TrySkipPreviousAsync`, 이전 곡 동작은 미디어 앱에 위임 |
| `com.spotify.superbird.volume.volume_up` | `{}` | 기본 출력 장치 `VolumeStepUp` |
| `com.spotify.superbird.volume.volume_down` | `{}` | 기본 출력 장치 `VolumeStepDown` |
| `com.spotify.superbird.volume.set_volume` | `{"volume": 0.4}` | 기본 출력 장치 `MasterVolumeLevelScalar` |

- 정상 응답: `[50, request_id, {}, {}]`
- 제어 거절·장치 예외: `[8, 48, request_id, {}, "wamp.error.runtime_error"]`
- 잘못된 볼륨 인자: `[8, 48, request_id, {}, "wamp.error.invalid_argument"]`
- 위 오류 URI는 현재 브리지 정책, 성공 여부를 무조건 응답하지 않고 Windows 반환값 반영
- 볼륨 설정값은 0~1의 유한 숫자로 검사, 자동 음소거 해제 없이 전달
- 볼륨 이벤트 payload: `{"volume": 0.4, "volume_steps": 51}`, 단계 수는 출력 장치 조회값
- APK 근거: `p/j7i0.java`, `p/i7i0.java`, `p/a351.java`, `VolumeAppProtocol$SetVolume`, `VolumeAppProtocol$VolumeState`
- 홈·상단 프리셋 화면의 shelf·presets 조회는 `com.spotify.superbird.graphql` 경로, 원시 버튼 이벤트와 구분
- 2026-10-20 버튼별 실기 시험: 외부 미디어 재생 화면에서 프리셋 1~4번 짧은 입력에 제어 RPC 없음, 홈/뒤로 입력에는 `shelf` 조회 3회 수신
- 현재 홈 조회 오류 응답 후 검은 화면 및 기기의 `query home failed` 보고 확인. 상세 조건과 원본은 [버튼 입력 관찰](CarThing_APK_분석_및_Windows_연결.md#프리셋홈-버튼-입력-관찰) 참조
- `Play Spotify` 터치 입력: `com.spotify.superbird.set_active_app`의 `{"app":"spotify"}` 요청 후 `com.spotify.superbird.resume`의 `{}` 요청 수신
- 현재 앱 전환 미지원 오류 및 재생 요청 오류 응답, 사용자 아이콘 깜빡임·화면 유지 확인. 상세 기록은 [터치 버튼 관찰](CarThing_APK_분석_및_Windows_연결.md#play-spotify-터치-버튼-관찰) 참조

## 7. 플랫폼 이식 구성

| 구성 요소 | 공통 책임 | 플랫폼 교체 지점 |
|---|---|---|
| 연결 | 인증된 대상의 바이트 스트림 확보 | RFCOMM 서버 생성·수락·종료 |
| 프로토콜 | 프레임·MessagePack·인증 단계·구독·RPC | OS 의존 없이 구현 |
| 미디어 공급자 | 앱 이름·곡 정보·재생 상태·위치 | OS 미디어 세션 API |
| 이미지 공급자 | 이미지 변환·ID 생성·캐시 | OS 이미지 디코더·인코더 |
| 실행 수명 | 취소·재연결·로그·직렬 송신 | 앱·서비스 수명 관리 |

- 제안 인터페이스: `Transport.read/write/close`, `MediaProvider.readState`, `ImageStore.put/get`
- Windows 코드의 `Program`, `MediaBridge`, `WindowsMedia`를 각각 위 경계에 대응
- Android 대응 후보: RFCOMM 서버는 `BluetoothAdapter.listenUsingRfcommWithServiceRecord("Spotify", UUID)`와 `BluetoothServerSocket.accept()` 사용. 연결 수락은 블로킹 작업이므로 UI 스레드와 분리. [Android Bluetooth 연결 문서](https://developer.android.com/develop/connectivity/bluetooth/connect-bluetooth-devices)
- Android 12 이상 대상: 페어링 장치 통신에 `BLUETOOTH_CONNECT` 런타임 권한, 장치 탐색 시 `BLUETOOTH_SCAN`, 휴대폰 검색 허용 시 `BLUETOOTH_ADVERTISE` 적용. [Android Bluetooth 권한 문서](https://developer.android.com/develop/connectivity/bluetooth/bt-permissions)
- Android 타 앱 미디어 조회 후보: `MediaSessionManager.getActiveSessions` 및 `MediaController`. 활성 세션 조회에는 `MEDIA_CONTENT_CONTROL` 권한 또는 사용자가 활성화한 알림 리스너 필요. [Android MediaSessionManager](https://developer.android.com/reference/android/media/session/MediaSessionManager)
- 여러 활성 세션 중 표시 대상 선정, 백그라운드 실행 수명, 권한 흐름은 Android 앱 설계 단계의 결정 항목
- Android 대응은 위 API 문서에 따른 이식 설계이며, 실기 표시 근거는 1절의 Windows 조건

## 8. 이식 시 검증 기준

| 단계 | 검사 기준·현재 근거 |
|---|---|
| 연결 | 광고 UUID·서비스 이름 일치, 지정된 페어링 기기만 수락 |
| 프레임 | 분할 읽기 조립, 길이 초과·잘린 프레임 오류 처리 |
| 세션 | HELLO → CHALLENGE → AUTHENTICATE → WELCOME 순서, 세션 상태 이벤트 |
| 구독 | 요청 ID 보존, 토픽별 구독 ID 사용, 해제 후 발행 중지 |
| 미디어 | 제목·아티스트·앱 이름·재생 상태·곡 변경 반영 |
| 이미지 | ID 일치, RESULT 인덱스 3의 맵, 실제 이미지 크기 일치 |
| 캐시 | 24개 고유 이미지 × 3회 조회, 최근 8개 보관 및 이전 항목 제거 |
| 실기 표시 | 다음 곡 전환 후 248×248 이미지 응답·화면 표시 확인 |
| 실기 제어 | 이전·다음 곡·일시정지·볼륨 증가·감소 사용자 동작 확인 및 RPC 수신·수락 기록 대조 |

- 자동 시험 진입점: `CarThingProbe.exe --self-test`
- 패킷 원본 및 실기 시험 경로: [분석·검증 문서](CarThing_APK_분석_및_Windows_연결.md)
- 원본 캡처는 로컬 보관 자료이며, 본 문서의 예제는 장치·사용자 식별값을 제외한 재작성 예제
