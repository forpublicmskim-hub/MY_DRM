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

## 설계

파일 시스템의 현재 스캔 결과를 관찰 상태의 기준으로 삼습니다. 감시 상태는 `WorkspaceProtectionState`와 분리하여 감시를 실제 DRM 보호로 오해하지 않도록 했습니다.

Application과 Platform 계층은 culture와 사용자 문구를 알지 않으며 enum과 관찰 데이터만 전달합니다. Desktop ViewModel이 `ILocalizationService`를 사용해 감시 상태와 관찰 종류를 현재 UI culture의 리소스로 변환합니다.

## 영향

등록된 로컬 Workspace는 Desktop 실행 중 자동으로 감시됩니다. 파일 내용과 Registry 형식은 변경되지 않으며 관찰 결과는 메모리에만 유지됩니다.

## 검증

- 실행 중인 Debug Desktop과 출력 파일 충돌을 피하기 위해 `dotnet build Drm.slnx -c Release --no-restore`를 실행했으며 경고 0개, 오류 0개로 통과했습니다.
- 기존 라이프사이클 테스트 9개와 localization 및 감시 통합을 포함한 Workspace 테스트 36개가 모두 통과했습니다.
- Workspace 감시 테스트는 초기 스캔, 파일 생성, 이름 변경의 이전 경로 보존 및 중지 후 상태를 검증합니다.

## 관련 문서

- [[workspace-registration]]
- [Workspace 파일 감시 설계](../../docs/workspace-monitoring.md)
