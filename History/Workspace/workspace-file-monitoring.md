# Workspace 파일 감시 기반

## 요약

등록된 Workspace에서 파일 시스템 변경을 관찰하는 기반을 추가했습니다. 관찰과 상태 조정만 수행하며 파일 보호나 접근 통제를 활성화하지 않습니다.

## 변경 사항

- 감시 상태와 관찰 이벤트의 Application 계약을 추가했습니다.
- 초기 전체 스캔과 `FileSystemWatcher` 변경 힌트를 결합했습니다.
- bounded channel 포화 또는 watcher 오류 시 전체 재스캔을 요청합니다.
- 등록 해제와 Desktop 종료 시 monitor를 정리합니다.
- 감시 상태와 관찰 종류의 사용자 문구를 영어 중립 및 한국어 위성 RESX 리소스로 제공하도록 localization 기반과 통합했습니다.
- 초기 스캔, 파일 생성, 중지 경로의 통합 테스트를 추가했습니다.
- Desktop OnClosed의 동기 DisposeAsync 대기를 제거하고 비동기 close coordinator로 교체했습니다.
- 중복 종료 요청은 하나의 정리 task를 공유하며, 정리 오류가 발생해도 창 종료는 완료하도록 했습니다.
- Desktop의 최근 파일 관찰 목록을 열 머리글과 행 구분선을 갖춘 표 형태로 변경했습니다. 좁은 창에서는 가로 스크롤로 모든 열을 확인할 수 있으며, 긴 경로와 판정 사유 및 작업공간 이름은 툴팁으로 전체 값을 확인할 수 있습니다.
- 보호 작업공간 목록과 최근 파일 관찰 표 사이에 가로 `GridSplitter`를 추가했습니다. 사용자는 경계를 위아래로 드래그하여 두 영역의 높이를 조절할 수 있습니다.

## 설계

파일 시스템의 현재 스캔 결과를 관찰 상태의 기준으로 삼습니다. 감시 상태는 `WorkspaceProtectionState`와 분리하여 감시를 실제 DRM 보호로 오해하지 않도록 했습니다.

Application과 Platform 계층은 culture와 사용자 문구를 알지 않으며 enum과 관찰 데이터만 전달합니다. Desktop ViewModel이 `ILocalizationService`를 사용해 감시 상태와 관찰 종류를 현재 UI culture의 리소스로 변환합니다.

## 영향

등록된 로컬 Workspace는 Desktop 실행 중 자동으로 감시됩니다. 파일 내용과 Registry 형식은 변경되지 않으며 관찰 결과는 메모리에만 유지됩니다.

최근 관찰 표는 시각을 밀리초 단위의 고정 형식으로 표시하고, 이벤트·경로·수집 상태·평가 상태·판정 사유·작업공간 정보를 명시적인 열로 구분합니다. 이 표시 변경은 관찰 및 inspection 처리 계약에는 영향을 주지 않습니다.

크기 조절 영역은 정책 패널 및 하단 명령 영역과 분리된 Grid에 배치했습니다. 보호 작업공간 목록에는 120px, 최근 파일 관찰 영역에는 100px의 최소 높이를 적용하여 한쪽 영역이 완전히 사라지는 상황을 방지합니다.

## 검증

- 실행 중인 Debug Desktop과 출력 파일 충돌을 피하기 위해 `dotnet build Drm.slnx -c Release --no-restore`를 실행했으며 경고 0개, 오류 0개로 통과했습니다.
- 기존 라이프사이클 테스트 9개와 localization 및 감시 통합을 포함한 Workspace 테스트 36개가 모두 통과했습니다.
- Workspace 감시 테스트는 초기 스캔, 파일 생성, 이름 변경의 이전 경로 보존 및 중지 후 상태를 검증합니다.
- 종료 coordinator의 비동기 완료·중복 요청·실패 경로 테스트를 포함한 Workspace 테스트 48개가 통과했습니다.
- Release Desktop을 dotnet run으로 시작한 뒤 정상 창 닫기를 수행했으며 Desktop과 부모 dotnet 프로세스가 모두 종료되고 exit code 0을 반환했습니다.
- 최근 관찰 표와 영역 크기 조절 변경 후 `dotnet build Drm.slnx -c Release --no-restore`를 실행했으며 경고 0개, 오류 0개로 통과했습니다.
- `dotnet test tests/Drm.Workspaces.Tests/Drm.Workspaces.Tests.csproj -c Release --no-build --no-restore`를 실행했으며 localization과 AXAML 리소스 검증을 포함한 테스트 62개가 모두 통과했습니다.

## 관련 문서

- [[workspace-registration]]
- [Workspace 파일 감시 설계](../../docs/workspace-monitoring.md)
