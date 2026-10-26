# Spotify 화면 전환 및 버튼 요청 시험

- 시험일: 2026-10-27
- 실행: `dist/carthing-connector/carthing-connector.exe --spotify-ui-experiment`
- 일반 실행: 인자 없이 EXE 실행, 기존 외부 미디어 모드 유지
- 시험 모드: Windows 미디어 상태의 `currently_active_application`을 `null`로 전송
- `set_active_app(app=spotify)` 성공 응답 및 플레이어 상태 재전송
- 기존 EXE 보관: `artifacts/releases/carthing-tray-before-spotify-ui-20261027/`

## 수신 요청 및 응답

| 조건 | 수신 요청 | 시험 응답 |
|---|---|---|
| 첫 시험 모드 연결 | `graphql`의 `shelf(limit:14 overrides:[])` | `data.shelf.items=[]` |
| 첫 시험 모드 연결 | `graphql`의 `presets(serial:...)` | 첫 실행 미지원 오류, 수정본 `data.presets.presets=[]` |

- 프리셋 조회 필드: `context_uri:contextUri`, `name`, `slot_index:slotIndex`, `description`, `image_url:imageUrl`
- 빈 프리셋 목록 조건의 시험, 저장된 프리셋 실행 조건과 구분
- 미지원 GraphQL 요청은 오류 응답과 원문 기록 유지
- 앱 로그: `%LocalAppData%\carthing-connector\logs\app-20261026-224210-913.log`, `app-20261026-224239-057.log`
- 패킷 로그: 같은 로그 폴더의 `captures/20261026-224239-255/`

## 자동 시험

| 항목 | 결과 |
|---|---|
| 시험 모드·일반 모드 분리 및 입력 상태 보존 | 통과 |
| 홈·프리셋 응답 및 알 수 없는 조회 거절 | 통과 |
| 이미지 24곡·곡당 3회 조회 | 통과 |
| 기존 제어 7개 경로 및 프로토콜 회귀 시험 | 통과 |

- 빌드 로그: `analysis/spotify-ui-publish.log`
- 시험 로그: `analysis/spotify-ui-self-test.log`, `analysis/spotify-ui-self-test-error.log`
## 활성 앱 상태 수정

- 최초 `id=spotify` 객체 전송에도 외부 미디어 화면 유지, 사용자 확인
- 공식 APK `p/cpn.java`의 `E(sm80)`는 외부 미디어용 `ActiveApp` 생성, `F(PlayerState)`는 Spotify 재생 상태에 `null` 사용
- 시험 모드의 해당 필드를 `null`로 수정 후 재빌드·재연결
- 일반 모드 앱 식별자 보존 및 시험 모드 null 상태 회귀 시험 통과
- null 상태 전송 후 순정 재생 화면 전환 사용자 확인, 앨범 이미지 미표시 보고
- 버튼 시험 시작 기준: `analysis/spotify-ui-button-check/checkpoint.json`

## 순정 화면 버튼 관찰

| 버튼·조건 | 수신 결과 |
|---|---|
| 프리셋 1번 짧게 약 2회, 빈 프리셋 목록 | 버튼 제어 요청 없음, permissions 처리 시간 진단 로그 1건 |
| 프리셋 2번 짧게 2회, 빈 프리셋 목록 | 화면 변화 없음, 수신 메시지 없음 |
| 프리셋 3번 짧게 입력, 빈 프리셋 목록 | 수신 메시지 없음 |
| 프리셋 4번 짧게 입력, 빈 프리셋 목록 | 화면 변화 없음, 야간 모드 진단 로그 1건, 버튼 제어 요청 없음 |

- 원본: `analysis/spotify-ui-button-check/preset-1.jsonl`, `preset-2.jsonl`, `preset-3.jsonl`, `preset-4.jsonl`

## 프리셋 1번 길게 누르기

- 화면: `Could not save` 프리셋 저장 실패 메시지, 사용자 확인
- 수신: `com.spotify.superbird.presets.set_preset`
- 인자: `{"presets":[{"context_uri":"spotify:context:fake","slot_index":1,"source":"tactile","version":1}]}`
- 응답: 미구현 RPC의 `wamp.error.no_such_procedure`, 기기의 `Failed to update preset items` 로그와 일치
- 입력 진단: `preset_one_button`, `interaction_type=long_hit`, `action_name=save_preset_content`
- 원본: `analysis/spotify-ui-button-check/preset-1-long.jsonl`
## 프리셋 4번 길게 누르기

- 사용자 선택에 따라 2번 대신 4번 시험
- `com.spotify.superbird.presets.set_preset` 요청 3회 수신
- 인자: `{"presets":[{"context_uri":"spotify:context:fake","slot_index":4,"source":"tactile","version":1}]}`
- 응답: 각 요청에 `wamp.error.no_such_procedure`
- 1번·4번에서 물리버튼 번호와 `slot_index` 일치 확인
- 원본: `analysis/spotify-ui-button-check/preset-4-long.jsonl`

## 순정 화면 홈 버튼

- 화면: 홈/뒤로 짧게 누른 뒤 검은 화면, 사용자 확인
- 수신: `com.spotify.superbird.get_home` 3회
- 인자: `{"limit":14,"limit_overrides":{}}`
- 응답: 각 요청에 `wamp.error.no_such_procedure`
- 기기 오류: `fetch home failed: Interapp error: wamp.error.no_such_procedure`
- 기존 외부 미디어 시험의 GraphQL `shelf` 조회와 다른 RPC 경로 확인
- 원본: `analysis/spotify-ui-button-check/home.jsonl`

## 프리셋 1번 빠른 두 번 입력

- 조건: 빈 프리셋 목록, 1번을 빠르게 두 번 누르도록 안내 후 사용자 입력 완료 보고
- 수신 결과: 관찰 구간 RX 메시지 0건
- 동일 캡처 세션 및 실행 프로세스 유지 확인
- 원본: `analysis/spotify-ui-button-check/preset-1-double.jsonl`

## 현재 APK 프리셋 API 정적 확인

- 대상 APK: `9.0.10.415`
- `p/hvk0.java`: `com.spotify.superbird.presets.get_presets`, `com.spotify.superbird.presets.set_preset` 등록
- `p/gvk0.java`, `p/evk0.java`: 조회·저장 모두 응답 목록을 `DevicePresets`로 변환
- `PresetsAppProtocol_DevicePresetsJsonAdapter.java`: 응답 필드 `result`, 목록 형식
- `p/c2z0.java`: 서버 프리셋 경로 `carthing-proxy/content/v1/presets/{device_id}`, 조회 v3 경로 포함
- 응답 항목 내부는 `List<Object>`로 처리, 기기 요구 필드의 추가 검증 필요

- 구버전 `8.8.28.409` 정적 비교: [구버전 APK API 비교](56_구버전_APK_API_비교.md)

## 채워진 프리셋 시험 준비

- 시험 모드에 `PC Preset 1`~`PC Preset 4` 구성, 슬롯별 `spotify:playlist:carthing-test-N` 시험 URI 사용
- 직접 조회·저장 RPC의 `result` 및 GraphQL 프리셋 조회에 동일 목록 반환
- 저장 요청의 슬롯·URI 검증 후 해당 슬롯 갱신, 연결 세션 내 유지
- 항목 필드: `slot_index`, `context_uri`, `name`, `description`, `image_url`; 이미지 URL은 빈 문자열
- 저장 응답·일반 모드 분리·잘못된 슬롯 포함 요청의 전체 갱신 거절 회귀 시험 통과
- 기존 이미지·제어·프로토콜 회귀 시험 통과
- 시험 로그: `analysis/spotify-presets-self-test.log`
- 교체 전 EXE: `artifacts/releases/carthing-before-filled-presets-20261027/`

## 프리셋 목록 표시 실기 확인

- 1번 `set_preset` 요청에 `result` 배열 4개 항목 반환
- 저장 대상 1번 URI: `spotify:context:fake`, 2~4번은 슬롯별 시험 URI 유지
- 기기에서 프리셋 4개 표시 사용자 확인
- 원본: `analysis/spotify-ui-button-check/filled-presets-save-1.jsonl`

## 채워진 프리셋 2번 짧은 입력

- 화면: `PC Preset 2` 선택 표시 사용자 보고
- 실행 요청: `com.spotify.play_item`, 인자 `{"feature_identifier":"car-thing-preset","id":"spotify:playlist:carthing-test-2"}`
- 같은 구간 실행 요청 2회, 진단 로그 `preset_two_button`·`key_stroke`·`action_name=play` 2건
- 후속 조회: `com.spotify.get_saved`, `com.spotify.get_children_of_item`, 대상 URI 동일
- 실행·저장 여부 조회 응답: `wamp.error.no_such_procedure`, 기기 오류 보고 수신
- 확인 결과: 빈 목록과 달리 채워진 프리셋의 짧은 입력에서 등록 URI를 포함한 실행 요청 수신
- 원본: `analysis/spotify-ui-button-check/filled-presets-short-2.jsonl`

## 순정 재생 제어 및 커버 매핑

- `com.spotify.skip_previous` → Windows 이전곡, `com.spotify.skip_next` → Windows 다음곡 연결
- 기존 `superbird.resume/pause`의 Windows 재생·일시정지 연결 유지
- 시험 모드의 `track.image_id`에 캐시 JPEG의 `data:image/jpeg;base64,...` URL 전송, 이미지 부재 시 빈 값
- 일반 모드의 이미지 캐시 ID 및 별도 이미지 RPC 유지
- 회귀 시험: 순정 이전·다음 요청의 단일 전달, 커버 URL의 MessagePack 왕복·일반 모드 보존·이미지 부재 처리 통과
- 기존 이미지·제어·프로토콜 시험 통과
- 빌드·시험 로그: `analysis/native-media-publish.log`, `analysis/native-media-self-test.log`
- 교체 전 EXE: `artifacts/releases/carthing-before-native-media-20261027/`

## 순정 제어 실기 결과 및 재시험

- 사용자 확인: 이전·다음곡 동작, 커버 미표시 및 일시정지·재생·좋아요 미동작
- 순정 일시정지·재생 요청: `com.spotify.set_playback_speed`, `playback_speed=0/1`; 미지원 오류 응답 확인 후 Windows 일시정지·재생 매핑 추가
- 좋아요 요청: `com.spotify.set_saved`, `saved=true`; 현재 미지원 오류 응답
- 커버 재시험 조건: data URL 유지, 제목·아티스트·앨범 기반 곡별 uid·합성 track URI 전송
- 합성 URI는 로컬 구분용 값, Spotify 카탈로그 ID와 구분
- 속도 0·1 매핑, 잘못된 값 거절, 곡별 식별자 변경 및 기존 회귀 시험 통과
- 원본: `analysis/spotify-ui-button-check/native-media-controls-and-cover.jsonl`

## 큐 기반 커버 전송 수정

- 사용자 확인: 일시정지·재생 정상, data URL 및 곡별 식별자 변경 후에도 커버 미표시
- 좋아요 연동은 사용자 요청으로 보류
- 웹앱 복원 소스 `component/Npv/PlayingInfo/Artwork.tsx`: 큐 항목의 `image_uri` 사용
- `store/ImageStore.ts`: 이미지 ID로 `get_image` 또는 `get_thumbnail_image` 요청, 응답 `image_data` 렌더링
- APK 큐 어댑터: `next/current/previous`, 항목 `uid/uri/name/artist_name/artists/image_uri/provider` 구조 일치
- 시험 모드에서 현재 곡만 포함한 `com.spotify.play_queue` EVENT 추가, 커버 변경 시 갱신
- data URL 변환 제거, 기존 캐시 ID 및 JPEG 응답 재사용, 썸네일 조회 RPC 지원 추가
- 큐 이미지 참조·이미지 RPC·커버 변경·중복 억제 회귀 시험 통과
- 시험 로그: `analysis/native-queue-self-test.log`
- 참고 소스: https://github.com/Merlin04/superbird-webapp/tree/e2e07d98a060f521ffabdbc6e0f18e6b12267e62

- 큐 수정 EXE 교체 후 재연결: `app-20261026-232142-712.log`
- 기기에서 `get_thumbnail_image` 및 `get_image` 요청 수신, 동일 캐시 ID로 JPEG 응답 확인

- 큐 이벤트·이미지 RPC 수정본에서 순정 재생 화면의 앨범 커버 표시 사용자 확인
- 커버 누락 수정: `player_state`만 전송하던 상태에서 `play_queue.current.image_uri` 및 이미지 조회 응답 연결

- 다음곡 전환 시 앨범 커버가 새 곡에 맞게 갱신되는 동작 사용자 확인
