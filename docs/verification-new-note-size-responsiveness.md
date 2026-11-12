# 새 메모 기본 크기와 편집 응답성 검증 (2026-11-12)

## 변경

- 설정 → 일반에서 새 메모의 가로·세로와 기본값 복원을 제공한다. 기본값 320×300 DIP, 최소 200×150 DIP이며 이후 생성하는 메모에 공통 적용한다.
- 기존 설정에 크기가 없으면 기존 기본값을 사용한다. 메모 DB 구조와 기존 메모 크기는 바꾸지 않는다.
- 작은 설정 창에서도 입력 영역을 스크롤할 수 있도록 최소 높이를 360 DIP로 확보했다.
- 검증 도구에서만 활성화하는 내부 측정 콜백으로 DB 대기·실행 시간과 저장 후 UI 갱신 시간을 수집한다. 본문을 진단 결과에 기록하지 않으며 일반 실행에서는 측정을 활성화하지 않는다.

## 결과

- Release 단위·STA 테스트 286개 통과. 잘못된 크기, 기본값 복원, 기존 설정 호환, 설정 저장·재로드, 화면 배율 100/150/200%와 화면 경계 보정을 포함한다.
- 실제 WPF 생성 경로에서 패널·원본 메모·외부 생성의 기본 크기 적용, SQLite 저장, 설정 변경 후 기존 메모 크기 유지를 확인했다. 일반 설정 화면을 PNG로 확인했다.
- 5분간 700줄 본문 메모 20개를 열고 목록·설정·패널을 숨긴 상태로 편집했다. 실제 TextBox.AppendText 4,677회, 저장 완료 211회, 첫 자동 백업 1회를 수행하고 최종 본문과 DB 내용 일치를 확인했다.
- UI heartbeat 최대 간격 206.35ms, p99 62.00ms. DB 저장 최대 실행 시간 95.94ms, 백업 21.74ms, 저장 후 UI 갱신 1.49ms. 1초 이상 정지는 없었다.
- 기존 12초 편집·패널 표시 시나리오와 숨겨진 목록 갱신 방지·저장 후 갱신 합치기 검증도 통과했다.

## 재실행과 한계

프로젝트에 포함된 .NET SDK 기준:

```powershell
.tools/dotnet/dotnet.exe test Memoit.slnx -c Release --no-restore
.tools/dotnet/dotnet.exe run --project scripts/SaveResponsivenessVerification -c Release -- --long --output artifacts/verification/save-responsiveness-long.json
.tools/dotnet/dotnet.exe run --project scripts/NewNoteHelpVerification -c Release -- artifacts/verification/new-note-size.json
```

검증 앱은 artifacts 아래에 별도 데이터를 만들며 개인 메모 DB를 사용하지 않는다. 5분 검증은 목록·패널 관련 기존 검사 후 편집 구간에서 패널을 숨기고 당일 첫 백업을 활성화한다.

물리 키보드·한글 IME, 사용자 데이터 전체, 실제 다중 모니터와 배율 전환은 이번 검증으로 입증하지 못했다. 응답 없음 현상은 재현하지 못했으며 추측성 성능 수정을 적용하지 않았다. 실제 앱에서 재발하면 버전·발생 시각·지속 시간·메모 수·본문 길이·IME 조합 중 여부를 먼저 기록하고, 해당 조건으로 검증 앱을 조정한다. 계속 재현될 경우 응답 없음 순간의 스레드 스택으로 UI 대기와 레이아웃·입력 처리를 구분해야 한다.
