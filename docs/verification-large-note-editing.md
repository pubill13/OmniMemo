# 대용량 메모 편집 성능 검증

2026-11-13, Windows x64 / .NET 10 Release. 개인 DB를 사용하지 않고 생성한 한글·영문·이모지 혼합 문서로 측정했다.

## 변경과 판단

기존 WPF TextBox의 자동 줄바꿈 레이아웃과 매 입력의 전체 본문 문자열 전달이 주요 병목이었다. 50,000줄·5,050,000자에서 전체 레이아웃이 끝나기까지 약 81초, 너비 변경 후 재배치에 약 30초가 걸렸다. 문자열 전달만 줄이는 수정으로는 레이아웃 병목을 제거할 수 없어 편집기를 AvalonEdit 6.3.1.120 하나로 교체했다. 전이 의존성은 추가되지 않는다.

모든 크기의 메모가 같은 편집기·문서 모델을 사용한다. 자동 줄바꿈, 글꼴, 여백, 색상, 얇은 스크롤바를 유지하고 링크 강조와 자동 들여쓰기를 끈다. 입력은 문서의 변경 부분을 수정하며, 전체 본문 문자열은 스냅샷을 요청하거나 저장할 때만 만든다. 큰 본문은 저장할 리비전의 불변 스냅샷을 캡처한 후 백그라운드에서 문자열로 변환한다. 기존 자동 저장 간격, 실패·재시도와 DB 스키마는 유지한다.

## 비교

동일 검증 도구의 초기 비교 결과다. 입력은 처음·중간·끝에서 각각 8회 수행했다. 값은 단일 실행의 측정값이며 통계적 성능 보장은 아니다.

| 문서 | 항목 | 기존 | 개선 |
|---|---|---:|---:|
| 10줄·1,010자 | 열기 및 레이아웃 완료 | 540ms | 521ms |
| 10줄·1,010자 | 입력 중앙값 / p95 | 2.04 / 3.62ms | 1.22 / 3.56ms |
| 10줄·1,010자 | 작업 집합 | 110.3MiB | 109.0MiB |
| 700줄·49,700자 | 열기 및 레이아웃 완료 | 1,572ms | 524ms |
| 700줄·49,700자 | 입력 중앙값 / p95 | 2.01 / 53.12ms | 1.03 / 3.22ms |
| 50,000줄·5,050,000자 | 열기 및 레이아웃 완료 | 80,789ms | 558ms |
| 50,000줄·5,050,000자 | 입력 중앙값 / p95 | 9.72 / 70.59ms | 1.38 / 4.33ms |
| 50,000줄·5,050,000자 | 일반 입력 1회 할당량의 대표값 | 약 30MB | 약 69KB |
| 50,000줄·5,050,000자 | 너비 변경 및 레이아웃 완료 | 29,681ms | 17ms |

레이아웃 완료 시간은 UI dispatcher가 ApplicationIdle 우선순위에 도달할 때까지다. 첫 화면이 나타나는 시간이나 그 시간 전체가 연속 UI 정지였다는 의미는 아니다. 작업 집합은 강제 GC 없이 측정한 프로세스 값이다. 첫 입력에는 JIT 등 초기화가 포함되어 개선 후 약 18ms까지 나왔다. 작은 문서의 반복 편집은 빨라졌지만 모든 개별 조작이 더 빠르다는 주장은 하지 않는다.

원본 비교 결과는 `artifacts/verification/large-note-before-{small,medium,50000}.json` 및 `large-note-after-{small,medium,50000,100000}.json`에 있다.

## 추가 검증

- Release 테스트 292개 통과: 대용량 입력 시 전체 문자열 미할당, 단일·일괄 저장 중 새 입력의 리비전 보존, 저장 실패 후 최신 내용 재시도, 제목 갱신, 선택 교체, 한글·이모지·탭·CRLF와 저장 후 실행 취소/다시 실행을 포함한다.
- 실제 WPF 창에서 5,050,000자 및 10,100,000자 문서의 대량 삽입·삭제·실행 취소·다시 실행, SQLite 저장, 백업 중 입력, DB 연결 재개방 후 전체 본문 일치를 확인했다.
- 반복 검사에서 5,050,000자 대량 삽입은 약 147~161ms, 저장은 약 85~274ms, 백업 중 입력은 약 1.6ms였다. 10,100,000자 대량 삽입은 약 238~277ms, 저장은 약 178~196ms였다. 이 저장 시간에는 백그라운드 작업이 포함된다.
- 기존 20개 메모·패널 표시·12초 자동 저장 검증도 통과했다. UI heartbeat 최대 간격 46.82ms, p99 45.78ms, 194회 편집·7회 저장 후 DB 본문이 일치했다.
- 5,050,000자와 10,100,000자 각각에서 100회 연속 입력과 실제 자동 저장 후 DB 일치를 확인했다. UI heartbeat 최대 간격은 각각 32.61ms, 32.36ms였다.
- 기존 화면 검증 테스트의 PNG에서 본문, 접힌 타일, 저장 실패 상태와 스크롤바를 확인했다.

## 재실행과 한계

```powershell
$env:DOTNET_CLI_HOME="$PWD\.dotnet-home"
.tools/dotnet/dotnet.exe test Memoit.slnx -c Release --no-restore
.tools/dotnet/dotnet.exe run --project scripts/LargeNoteVerification -c Release -- artifacts/verification/large-note.json 50000 100
.tools/dotnet/dotnet.exe run --project scripts/LargeNoteVerification -c Release -- artifacts/verification/large-note-small.json 10 100
.tools/dotnet/dotnet.exe run --project scripts/LargeNoteVerification -c Release -- artifacts/verification/large-note-long-lines.json 50 10000
.tools/dotnet/dotnet.exe run --project scripts/SaveResponsivenessVerification -c Release -- --output artifacts/verification/save-responsiveness-large-editor.json
```

최종 검증 데이터는 `artifacts/verification/large-note-final-*.json`, 20개 메모 검증은 `save-responsiveness-large-editor.json`에 있다. 검증 도구는 출력 경로 아래에 별도 DB·백업 폴더를 만든다.

한 줄이 10,000자처럼 매우 길면 자동 줄바꿈을 위해 그 논리 줄을 다시 배치하는 비용이 남는다. 50줄·500,050자 검사에서 입력은 약 58~101ms, 중간·끝 이동은 약 412~460ms였다. 줄바꿈 없는 수백만 자 단일 줄이 항상 빠르다는 보장은 하지 않는다.

한글 입력은 문서 데이터와 WPF TextArea 입력 경로로 확인했다. 물리 키보드의 Windows 한글 IME 조합·후보 창, 실제 OS 클립보드 및 물리 다중 모니터·배율 전환은 수동 검증하지 않았다.

## 1.13.5 배포 검증

Windows x64 자체 포함 단일 실행 파일의 버전은 1.13.5이며 AvalonEdit와 라이선스가 배포물에 포함된다. 개인 데이터와 분리한 `scripts/SmokeTest.ps1` 실행에서 UI Automation을 통한 한글·이모지 입력, 자동 저장, 36 DIP 접기, 프로세스 재시작 후 본문·상태 복원, 펼치기, 숨기기와 중복 실행 전달을 확인했다. 결과는 `artifacts/verification/release-1.13.5/smoke-*/results.txt`에 있다.
