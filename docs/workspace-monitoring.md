# Workspace 파일 감시

Desktop 애플리케이션은 등록된 Workspace의 기존 항목을 스캔하고 생성, 변경, 삭제, 이름 변경을 `WorkspaceObservation`으로 정규화합니다.

`FileSystemWatcher` 이벤트는 변경 힌트로 취급합니다. watcher 오류나 bounded channel 포화가 감지되면 `Degraded` 상태로 전환하고 전체 재스캔으로 현재 상태를 다시 조정합니다.

감시 상태는 등록 상태와 보호 상태에서 분리됩니다. `Watching`은 암호화나 접근 통제가 활성화되었다는 의미가 아닙니다.

## 계층과 수명

- `Drm.Application`: 감시 계약과 `WorkspaceMonitorManager`
- `Drm.Platform.Local`: `FileSystemWatcherWorkspaceMonitor`와 `LocalWorkspaceScanner`
- `Drm.Desktop`: Registry 목록과 monitor 조정 및 최근 관찰 결과 관리

OS callback은 bounded channel에 빠르게 기록합니다. canonical Workspace 경계 밖 경로와 reparse point는 발행하지 않습니다. 등록 해제 시 해당 monitor를 중지하고 애플리케이션 종료 시 모든 monitor를 폐기합니다.

Desktop 창 종료는 첫 close 요청을 잠시 보류하고 비동기 정리를 시작합니다. 정리 중 UI dispatcher는 계속 실행되므로 이미 queue에 들어간 관찰 callback과 monitor 종료가 서로 기다리는 교착을 만들지 않습니다. 정리가 성공하거나 오류로 끝나면 두 번째 close 요청으로 실제 창을 닫으며, 중복 close 요청은 하나의 정리 task를 공유합니다.

감시 상태와 관찰 종류는 Application enum으로 전달하고 Desktop에서 `ILocalizationService`를 통해 표시합니다. 영어 중립 리소스와 `ko-KR` 리소스는 동일한 감시 키 계약을 제공하며, Application과 Platform 계층에는 사용자 문구를 두지 않습니다.

## 현재 제한 사항

- 파일을 암호화, 삭제, 이동 또는 수정하지 않습니다.
- 다른 프로세스의 파일 접근을 차단하지 않습니다.
- `Created`는 파일 쓰기 완료를 보장하지 않습니다.
- 감시는 Desktop 프로세스 수명에 종속됩니다.
- 관찰 결과는 메모리에만 유지되며 감사 로그가 아닙니다.

## 검증

- 초기 스캔에서 기존 파일 발견
- 감시 시작 후 새 파일 생성 관찰
- 중지 후 monitor 상태 유지
