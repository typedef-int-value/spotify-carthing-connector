# carthing-connector 트레이 앱

- 2026-10-27 C# 브리지의 Windows 트레이 앱 전환
- 배포 파일: [carthing-connector.exe](../dist/carthing-connector/carthing-connector.exe)
- 실행: EXE 더블클릭, 콘솔·PowerShell 실행 명령 없이 트레이 상주
- 첫 실행의 페어링 장치 선택 및 이후 선택값 재사용
- 장치 검색: Windows 페어링 목록의 주소 속성 사용, 연결 가능한 장치의 실시간 상태와 구분

| 항목 | 구성 |
|---|---|
| UI | WinForms `NotifyIcon`·`ApplicationContext` |
| 중복 실행 | 같은 Windows 세션의 명명된 Mutex, 추가 실행 즉시 종료 |
| 트레이 메뉴 | 연결 상태·장치 선택·화면 모드·연결 다시 시작·로그 폴더 열기·종료 |
| 연결 상태 | 준비 중·연결 대기·Bluetooth 연결됨·오류 |
| 설정 | `%LocalAppData%\carthing-connector\settings.json` |
| 로그 | `%LocalAppData%\carthing-connector\logs\` |
| 패킷 기록 | 로그 폴더의 `captures/<UTC 시각>/` |
| 배포 | Windows x64, .NET·WinForms 런타임 및 의존 라이브러리를 포함한 단일 EXE |
| 네이티브 의존성 | 실행 시 .NET이 사용자 임시 폴더에 자동 추출 가능 |

## 검증 결과

| 항목 | 결과 |
|---|---|
| 단독 배포 | EXE 1개만 복사한 별도 폴더에서 실행 |
| 배포 크기 | `carthing-connector.exe` 142,397,190 bytes |
| 장치 선택 UI | 페어링 장치 4개 조회, Car Thing Q61S 선택 및 주소 저장 |
| 중복 실행 | 두 번째 실행 종료 코드 0, 실행 프로세스 1개 유지 |
| 설정 재사용 | 재실행 시 선택 창 없이 저장된 Car Thing 주소로 연결 준비 |
| 기존 회귀 시험 | 이미지 24곡·72회, 7개 제어 경로, 프로토콜·오류 처리 통과 |
| Bluetooth 연결 조건 | 저장된 페어링 주소 조회 후 Windows가 유효한 페어링 대상으로 열지 못해 오류 상태 표시 |

- 시험 원본: `analysis/tray-build.log`, `analysis/tray-publish.log`, `analysis/tray-self-test.log`
- 선택 저장 및 오류 상태 원본: 사용자 데이터 폴더의 `logs/app-*.log`
- 기존 실기 기능 기준: [기준 버전 기록](53_현재_동작_기준_버전.md)

## 재페어링 및 종료 문제 확인

- 사용자 보고: 오류 발생 후 트레이 종료 불가, Windows에서 Car Thing 재페어링
- 관찰: 장치 목록 조회 이후 선택 창이 표시되지 않은 상태, 선택 처리 중 `busy` 값에 따라 종료 메뉴 비활성화하는 코드 확인
- 기존 실행 조건: `Start-Process -WindowStyle Hidden`, 숨겨진 선택 창 여부는 원인 후보
- 조치: 해당 프로세스 종료 후 일반 앱 실행 방식으로 재시작
- 재페어링 장치 조회: `30E3D604D71F`, `Car Thing (SN: Q61S)`, Windows 상태 `Connected`
- 재시작 결과: RFCOMM 서비스 광고 성공 및 연결 대기 상태 진입
- 원본: `analysis/tray-repair-devices.log`, 사용자 로그 폴더의 재시작 시각 `app-20261026-222924-*.log` 참조

## 종료 처리 수정 및 재연결 검증

- 장치 검색·선택 중 종료 메뉴 활성화 유지
- 종료 시 장치 검색 취소 및 선택 창 닫기
- 연결 정리와 취소 콜백의 대기 시간 3초 제한
- 연결 정리 시간 초과 시 트레이 종료, 재시작 요청에서는 기존 연결과 중복 실행 방지
- 수정 EXE 교체 후 Car Thing Q61S의 Bluetooth 연결, WAMP 인증 및 14개 구독 확인

| 시험 조건 | 결과 |
|---|---|
| 작업 중 종료, 연결 작업 완료 상태 | 11 ms, 선택 창 객체 해제 및 메시지 루프 종료 |
| 작업 중 종료, 연결 작업 무응답 | 3,009 ms, 시간 제한 후 종료 |
| 취소 콜백 무응답 | 100 ms 제한의 TimeoutException 확인 |
| 기존 프로토콜·이미지·제어 회귀 시험 | 종료 코드 0 |
| 배포 파일 무결성 | 빌드 산출물과 배포 EXE의 SHA256 일치 |

- 시험 로그: `analysis/tray-final-tray-self-test.log`, `analysis/tray-final-self-test.log`
- 연결 로그: `%LocalAppData%\carthing-connector\logs\app-20261026-223358-349.log`
- 종료 회귀 시험 명령: `carthing-connector.exe --tray-self-test`
## 배포 파일 생성

```powershell
dotnet publish windows-probe/CarThingProbe.csproj -c Release -r win-x64 --self-contained true -o dist/carthing-connector
```

- 배포 폴더의 `carthing-connector.exe`만 다른 폴더·PC로 복사 후 실행
- 설정·로그는 EXE 위치와 무관하게 현재 사용자 데이터 폴더에 저장
- 다른 PC에서는 Windows Bluetooth 페어링 후 앱에서 장치 선택
- `--self-test`, `--media`, `--volume`, `--list`, `--bridge` 등 기존 진단 인자 유지
- 실행 스크립트 `Start-CarThing.ps1` 제거, EXE 직접 실행으로 통일

## 화면 모드 선택

- 트레이의 `화면 모드 → 외부 미디어 / Spotify` 선택 및 현재 모드 체크 표시
- 모드 변경 시 기존 연결 종료 후 재연결, 종료 시간 초과 시 모드 변경 중단
- `settings.json`의 `SpotifyMode`에 선택값 저장, 다음 실행에서 복원
- 기존 장치 주소만 있는 설정은 외부 미디어 모드로 해석
- 두 모드 모두 Windows 미디어 세션의 곡 정보·커버·제어 사용
- 기존 `--spotify-ui-experiment` 인자는 해당 실행의 Spotify 모드 지정용으로 유지
| 자동 시험 | 결과 |
|---|---|
| 두 모드 전환·기존 연결 취소 순서 | 통과 |
| 모드 설정 저장·역직렬화 및 기존 설정 호환 | 통과 |
| 작업 중 종료·연결 무응답 시 종료 | 8 ms / 3,007 ms |
| 프로토콜·이미지·재생·볼륨·셔플 회귀 시험 | 종료 코드 0 |

- 빌드·시험 로그: `analysis/tray-modes-publish.log`, `analysis/tray-modes-tray-test.log`, `analysis/tray-modes-test.log`
