## 2026-10-28 추가 인계 — 1.13.0 작업

- 구현 범위: 기존 패널은 ‘패널’로 명명하고, 헤더 축소 버튼으로 240×88 DIP 2줄 아이콘 미니패널로 전환한다. `LayoutSettings.OverlayCompact`가 마지막 모드를 저장한다.
- 미니패널은 새 메모·목록·전체 상태 조작·정돈·압정·설정·일반 복귀·숨기기를 제공한다. 색상별 조작은 일반 패널에 남긴다.
- 트레이 더블클릭은 목록이 아닌 마지막 모드의 패널을 표시하고 앞으로 가져온다. 미니 모드에서 페이지는 별도 페이지 창으로 제공한다.
- 삭제 실행 취소는 미니패널에 묻히지 않도록 같은 `UndoNotice`를 목록 또는 별도 창으로 이동해 남은 시간을 유지한다.
- 자동 검증: `artifacts/verification/compact-panel-1.13.0.json` 성공. 240×88·상태/좌표/압정/색상·설정 재로드·NotifyIcon 더블클릭 핸들러·페이지·실행 취소·100/150/200% PNG 확인. 물리 트레이 마우스 더블클릭과 실제 DPI 전환은 미검증.
## 2026-10-26 추가 인계 — 1.12.2

- 최신 코드/배포 버전: 1.12.2. master 커밋 `483c9f6665d1353f83aef6779483348321d9a50f`.
- 모두 보이기·접기·펼치기·두 정돈은 실행 순간 표시 대상 메모를 앞으로 올린다. 압정과 상태/정돈 구분 유지. 다른 앱의 항상 위 창보다 위에 고정하지 않는다.
- 자동 저장 후 숨겨진 목록 재생성 제거, 저장 콜백 병합, 변하지 않은 패널/페이지 컨트롤 유지, 긴 본문 제목 할당량 개선.
- 253개 테스트 통과. 실제 WPF Z-order 통합 검사 통과. 분리된 wrapper에서 실제 접기·펼치기·보이기 클릭 및 화면 확인.
- 700줄 본문 20개, 12초 편집/자동 저장 시 최대 heartbeat 간격 165ms. 개인 데이터의 원래 1~2초 멈춤은 직접 재현하지 않았으므로 완전 해결로 단정하지 않는다. 상세: docs/verification-1.12.2.md.
- ZIP: artifacts/OmniMemo-1.12.2-win-x64.zip. SHA256: 9EF285E11C43984D1E85AC83EC32ED7F4FD4CFF278CB139A5561F75B98134670.
- 소스 master 업로드 완료. 게시 스크립트: artifacts/PublishRelease-1.12.2.ps1.
- 아래 내용은 이전 1.12.1 인계 기록이다. 위 최신 기록과 실제 코드가 우선한다.

# OmniMemo 세션 인계

기준: 2026-10-26 (한국 시간). 이 문서는 다음 작업을 위한 상태 기록이며, 새 사용자 요청과 실제 Git/코드 상태가 우선한다.

## 현재 완료 상태

- Windows 개인용 로컬 스티커 메모. C# / .NET 10 / WPF / SQLite.
- 최신 완료 버전: **1.12.1**. 설정 화면 UI 개선까지 구현·검증·GitHub 게시 완료.
- 저장소: https://github.com/pubill13/OmniMemo
- 릴리스: https://github.com/pubill13/OmniMemo/releases/tag/v1.12.1
- master 커밋: `e18093ca5710bea0c6fc6ea7bc19f208f710a5f5`.
- 직전 1.12.0: `114fd0aae55ba69542b66ee60b5a96df78777aec`.
- 인계 문서 작성 직전 작업 트리는 깨끗했다. 이 인계 문서와 NEXT_SESSION_PROMPT.md는 이번 요청으로 추가한 로컬 문서다.
- **현재 미완료로 확정된 구현 작업은 없다.** 이전 긴 대화의 완료된 계획을 다시 구현하지 않는다. 다음 요청 없이 새 기능을 임의로 시작하지 않는다.

## 사용자 선호

- 작은 옵션까지 계속 묻지 말고 합리적으로 구현·검증해 마무리한다.
- 메모 목록 수준의 정돈된 UI를 선호한다. 투박한 기본 컨트롤, 붙어 있는 버튼, 불필요한 알림을 싫어한다.
- 정상 새로고침에 ‘새로고침 완료’ 같은 알림을 띄우지 않는다.
- 코드 변경을 배포할 때 소스 업로드와 GitHub 릴리스를 함께 한다는 요청이 반복 확정되었다.
- 검증했다고 과장하지 않는다. 실제 클릭, 자동화 테스트, 물리 다중 모니터/배율 확인을 구분한다.
- 개인 메모를 테스트 데이터로 사용하지 않는다. 시연 중에는 무엇을 확인하는지 짧게 설명한다.

## 유지해야 할 확정 동작

| 명령 | 동작 |
|---|---|
| 모두 접기 | 현재 왼쪽 위에서 접기. 정렬하지 않음 |
| 모두 펼치기 | 타일 현재 위치에서 펼치기. 정렬하지 않으며 겹침 허용 |
| 접어서 정돈 | 모두 접고 모니터별 타일 시작점·형태·간격·순서로 정렬 |
| 펼쳐서 정돈 | 선택 모서리에서 실제 크기로 정렬. 공간 부족은 겹침 없는 페이지로 분할 |
| 모두 숨기기 / 보이기 | 접힘·좌표·크기·압정·페이지를 유지하며 표시만 변경 |

- 상태 변경과 정돈을 다시 합치지 않는다. 접기·펼치기는 페이지를 해제하고 페이지에 속한 대상도 처리한다. 사용자 숨김과 휴지통은 제외한다.
- 개별 접기: 현재 펼친 창의 왼쪽 위에 타일. 과거 정돈 좌표로 돌아가지 않는다.
- 개별 펼치기: 타일 위치에서 시작하고 필요한 화면 경계 보정만 한다.
- 입력/드래그 자동 재배치 없음. 자석은 더 끌면 풀려 화면 일부 걸침·모니터 간 이동 가능.
- 압정 활성은 청록색 내부 채움, 비활성은 회색 윤곽. 제목 클릭/드래그와 압정 클릭 분리.
- 패널: 메모 / 전체 조작 / 정돈 구획. 색상별 조작과 페이지는 보조 영역. 투명도 기능 제거, 100% 불투명.
- 트레이: 숨기기·보이기 별도 항목. 정돈 버튼 없음. 미니 패널은 한 번 클릭으로 표시. 저장 다시 시도는 실패 시에만 표시.
- 메인 목록은 활성 메모 전용. 전체 메모 버튼은 검색·선택을 보존해 새로고침. 휴지통은 별도 창.
- 새 메모는 같은 모니터의 같은 색 마지막 메모 옆에서 펼쳐 생성. 색상은 메모 ＋=원본, 패널=선택색, 나머지=마지막 활성 색. 세부 경계 처리는 README/코드 참조.
- 기본 단축키 8개: 새 메모 N, 목록검색 F, 전체 표시 H, 전체 접기/펼치기 C, 패널 P는 Ctrl+Alt+Shift. 개별 접기/펼치기는 Ctrl+Shift+Space. 두 정돈 키는 기본 미지정.
- 설정 v6, DB v3. 1.12.1에서는 형식 변경 없음. 설정 적용은 저장만 하고 정돈하지 않는다.

## 1.12.1 변경 파일

- `src/Memoit/Views/LayoutOptionsWindow.cs`: 640×740 기본 설정 창, 제목/설명, 밝은 배경, 흰 카드, 탭, 하단 적용 영역, 구획별 단축키 카드, 버전 안내.
- `src/Memoit/Views/SettingsStyles.xaml`: 둥근 저대비 ComboBox와 모서리 RadioButton 스타일.
- `src/Memoit/Memoit.csproj`: 버전 1.12.1.
- `README.md`: 버전 및 변경 내용.
- 기존 저장·배치 계산은 변경하지 않았다.
- 커스텀 ComboBox에서 모니터 객체 문자열이 표시되는 문제를 실제 화면에서 발견해 수정했다. `SelectionBoxItemTemplate`뿐 아니라 `ItemTemplateSelector`도 ContentPresenter에 연결해야 DisplayMemberPath가 유지된다.

## 코드 길잡이

- 통합 설정은 `Views/LayoutOptionsWindow.cs`이다. 이름만 보고 별도 `SettingsWindow`를 잘못 수정하지 않는다.
- `App.State.cs`: 상태 일괄 변경. `App.Layout.cs`: 정돈. `App.Pages.cs`: 페이지.
- `App.Overlay.cs`, `Views/OverlayPanelWindow.cs`: 패널.
- `Views/NoteWindow.xaml(.cs)`: 메모 창/압정/개별 전환/드래그.
- `App.NewNote.cs`, `Services/NotePlacement.cs`: 새 메모 위치.
- `App.Trash.cs`, `App.TrashWindow.cs`, `Services/TrashOperations.cs`: 삭제/복원.
- `Services/LayoutSettings.cs`, `HotkeyDefaults.cs`, `HotkeyService.cs`: 설정과 단축키.
- `Services/NoteStore.cs`, `ViewModels/NoteViewModel.cs`: 저장·자동 저장.
- `Services/NoteArrangement.cs`, `TileLayout.cs`, `MonitorCatalog.cs`: 순수 배치/모니터.
- `tests/Memoit.Tests`: xUnit와 STA WPF 테스트. 관련 테스트 이름은 파일 목록에서 확인한다.

## 검증 결과와 한계

- 1.12.1 최종 Release 테스트 **243 통과, 실패 0, 건너뜀 0**.
- Windows x64 자체 포함 게시 빌드 성공.
- 별도 설정 검증 창에서 카드/탭/단축키/모니터 드롭다운 표시와 클릭 확인.
- 물리 다중 모니터, 150/200% 배율은 이번 UI 변경에서 실측하지 않음. 모든 메뉴의 모든 버튼을 이번 버전에서 다시 검사했다고 주장하지 않는다.
- 과거 1.12.0 성능: 100% 화면에서 100타일 정돈 45~99ms, 20메모 접기 56~90ms, 펼치기 162~214ms. 일부 펼치기는 200ms 목표 초과. 새 버전 실측 수치로 재사용하지 않는다.
- README의 1.12.0 당시 실제 클릭 미완료 기록은 역사 기록이다. 이후 별도 wrapper 수정 후 압정/패널 클릭을 확인했다. `artifacts/verification/ui-followup-1.12.txt` 참고.

## 빌드·테스트

PowerShell, 프로젝트 루트에서 실행:

```powershell
$env:DOTNET_CLI_HOME="$PWD\.dotnet-home"
& .tools/dotnet/dotnet.exe test Memoit.slnx -c Release --no-restore -p:UseSharedCompilation=false
& .tools/dotnet/dotnet.exe publish src/Memoit/Memoit.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:UseSharedCompilation=false -o artifacts/OmniMemo-1.12.1/win-x64
```

- SDK/캐시/네트워크 권한 오류는 해당 명령의 승인 요청으로 처리한다. 성공 출력이 없으면 통과로 추정하지 않는다.
- Git safe-directory 오류 시 `git -c safe.directory=C:/Users/Lim/Desktop/프로젝트모음/메모잇 ...` 사용.
- README 개발 예시에는 아직 publish 출력 폴더 1.12.0이 남아 있다. 다음 배포 때 실제 버전에 맞춰 갱신 가능하며 기능 오류는 아니다.

## 데이터와 실제 실행 주의

- 개인 DB 기본 경로: `%LOCALAPPDATA%\MemoitPersonal\notes.db`. 실행 파일 폴더와 별개.
- 검증에는 `OMNIMEMO_DATA_DIR`로 프로젝트 `artifacts/verification/...` 아래 독립 폴더를 지정한다. 호환 변수 `MEMOIT_DATA_DIR`도 있으므로 실행 전에 코드를 확인한다.
- 개인 데이터 삭제/초기화 금지. 테스트용 프로세스만 식별하여 종료한다.
- UI 자동화 전 제공된 computer-use 스킬/도구 지침을 읽는다. 이전 세션의 창 ID나 변수는 재사용하지 말고 새로 조회한다.
- 과거 시작 실패의 한 원인은 검증용 wrapper의 `Assets/OmniMemo.ico` 누락이었다. wrapper 리소스를 포함해 해결했다. WPF 초기화 후 `Application.ResourceAssembly`를 강제로 지정하면 별도 예외가 나므로 사용하지 않는다.
- 사용자가 봤던 timeout 대화상자까지 모두 같은 원인이었다고 단정하지 않는다. 재발하면 당시 상세 오류/로그를 확인한다.

## 로컬 배포·검증 산출물 (Git 추적 제외)

- `artifacts/OmniMemo-1.12.1/win-x64/OmniMemo.exe`
- `artifacts/OmniMemo-1.12.1-win-x64.zip`: 76,998,041 bytes.
- ZIP SHA256: `A347FB50CE3F0EE7F96A5DCF1C4A8F7638709D047F148D47B60E816FB67A55EB`.
- `artifacts/PublishRelease-1.12.1.ps1`: remote master SHA 검사 → draft 생성 → ZIP 업로드 → 게시 → 크기/제공 digest 검증. 자격 증명은 Git credential에서 내부적으로 얻으며 출력하지 않는다.
- 게시 완료 후 소스/자산 검증 성공. 1.12.1을 다시 게시할 필요 없음.
- `artifacts/SettingsPreview/checked/SettingsPreview.exe`: 개인 DB 미접근 설정 프리뷰. 최종 버전 문구와 입력란 수직 정렬 변경 전 빌드이므로 배포물로 오인하지 않는다.
- `artifacts/PinVerification/publish-ready/PinVerification.exe`: 과거 압정 검증용 wrapper.
- 새 배포는 버전 변경 → 테스트 → 커밋 → 해당 커밋으로 publish → ZIP → 소스 푸시/릴리스 검증 순서. 자산 덮어쓰기나 태그 강제 이동 금지.

## 다음 세션 시작

1. AGENTS.md, 이 문서, README 및 최신 사용자 요청을 읽는다.
2. Git 상태·현재 버전·마지막 커밋 확인. 인계 문서 외 새 변경이 있으면 보존한다.
3. 완료된 1.12.1 작업을 반복하지 않고 새 요청 범위를 정한다.
4. 실제 수정한 범위만 검증·리뷰하고, 배포 요청/기존 선호에 맞춰 릴리스한다.
5. 사용자 요청이 없으면 현 상태를 짧게 확인하고 다음 요청을 기다린다.
