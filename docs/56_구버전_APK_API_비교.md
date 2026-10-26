# Spotify 구버전 APK의 Car Thing API 비교

- 분석일: 2026-10-27
- 비교 대상: 사용자 제공 `8.8.28.409` 및 기존 `9.0.10.415` APK
- 구버전 SHA-256: `257605D3CE292E6831A2DF2846D814C55772A6F7C6336A73874979F24D73E82C`
- 분석 방식: DEX 문자열 테이블 직접 비교 및 JADX 1.5.6 디컴파일

## 비교 결과

| 항목 | 8.8.28.409 | 9.0.10.415 |
|---|---|---|
| `com.spotify.superbird.*` 접두어 문자열 집합 | 47개 | 47개, 동일 집합 |
| 프리셋 조회 | `presets.get_presets` | 동일 |
| 프리셋 저장 | `presets.set_preset`, 입력 `presets` 배열 | 동일 |
| 프리셋 조회·저장 응답 | `result` 배열 | 동일 |
| 프리셋 항목 역직렬화 | `List<Object>`, 서버 JSON 목록 변환 | 동일 |
| 홈 조회 | `get_home`, `limit`·`limit_overrides` | 동일 |
| 홈 응답 | `items` 배열 | 동일 |
| 홈 묶음 항목 | `uri`·`title`·`total`·`children` | 동일 |
| 홈 재생 항목 | `uri`·`title`·`subtitle`·`image_id` | 동일 |
| 프리셋 서버 경로 | content/v1 저장·조회, content/v3 조회 | 동일 |
| Spotify 재생의 활성 앱 값 | null | null |

- 문자열 집합 비교는 API 이름 존재 여부의 근거, 모든 구현 동작의 동일성 판정과 구분
- 구버전 전용 프리셋·홈 API 이름 추가 없음
- 프리셋 내부 항목은 서버 응답 객체를 전달하는 구조, 필수 필드 확정에는 기기 응답 검증 필요
- `get_home`의 직접 응답 `items`와 GraphQL 응답 `data.shelf.items` 구분
- JADX 구버전 전체 처리 결과: 오류 125건, 종료 코드 3; 전체 소스 복원 성공으로 해석 불가

## 코드 근거

| 역할 | 구버전 | 기존 버전 |
|---|---|---|
| 프리셋 API 등록 | `analysis/jadx-8.8.28/sources/p/qiu.java` | `analysis/jadx/sources/p/hvk0.java` |
| 조회·저장 처리 | `p/piu.java`, `p/oiu.java` | `p/gvk0.java`, `p/evk0.java` |
| 프리셋 서버 경로 | `p/vr10.java` | `p/c2z0.java` |
| 홈 API 등록 | `p/vci.java` | `p/l0z.java` |
| Spotify 재생 상태 | `p/yp1.java` | `p/cpn.java` |

- 응답 모델: 각 분석 폴더의 `sources/com/spotify/superbird/interappprotocol/{presets,home}/model/`
- DEX 비교 스크립트·결과: `analysis/compare-superbird-dex.py`, `analysis/superbird-api-dex-comparison.json`
- 디컴파일 로그: `analysis/jadx-8.8.28-cli.log`
- 관련 실기 기록: [Spotify 화면 전환 시험](55_Spotify_화면_전환_시험.md)