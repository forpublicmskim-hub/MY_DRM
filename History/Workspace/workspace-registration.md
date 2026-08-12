# 작업공간 등록과 Avalonia 관리 화면

## 요약

파일을 변경하지 않고 로컬 폴더를 DRM 보호 대상으로 등록·조회·등록 해제하는 첫 작업공간 마일스톤을 구현했다. 등록 상태와 실제 보호 상태를 분리하고, Desktop UI가 경로 정책이나 JSON 저장소를 직접 소유하지 않도록 계층 경계를 추가했다.

## 변경 사항

- `WorkspaceId`, `WorkspaceLocation`, `ProtectedWorkspace`, 등록·보호 상태와 구조화된 검증 코드를 도메인에 추가했다.
- 플랫폼 위치 해석을 위한 `IWorkspaceLocationResolver`와 경로 열기용 `IWorkspacePathLauncher`를 추가했다.
- 등록 변경 직렬화, 동일·부모·자식 경로 거부, 등록 조회와 파일 시스템에 영향을 주지 않는 등록 해제를 `WorkspaceService`로 구현했다.
- 스키마 버전, 손상 감지, 임시 파일 기록, 디스크 flush와 파일 교체를 사용하는 `JsonWorkspaceRegistry`를 추가했다.
- 로컬 고정 디스크만 허용하고 루트, 시스템, 애플리케이션, 설정, 임시, 네트워크, 이동식, 알려진 클라우드 동기화 및 symlink/reparse point 위치를 거부하는 플랫폼 resolver를 추가했다.
- Avalonia 12와 CommunityToolkit MVVM 기반 `Drm.Desktop` 프로젝트를 추가해 폴더 선택, 목록, 접근 상태, 등록 해제, 폴더 위치 보기를 제공했다.
- UI에 등록이 파일 보호를 의미하지 않으며 `WorkspaceProtectionState.NotActivated` 상태임을 명시했다.

## 설계

폴더 선택은 Avalonia `StorageProvider`, 위치 해석은 플랫폼 adapter, 중복·중첩 판단은 Application 정책, 영속성은 Registry가 담당한다. UI는 `WorkspaceService`가 성공 결과를 반환한 후 목록을 다시 읽으므로 저장 실패 항목을 성공으로 표시하지 않는다.

경로 문자열은 `WorkspaceId`와 분리했고 `WorkspaceLocation`에 향후 플랫폼 파일 식별자와 sandbox bookmark를 담을 확장 지점을 두었다. 현재 등록은 보호 활성화와 분리되어 어떤 기존 파일도 암호화·삭제·덮어쓰기하지 않는다.

## 영향

- 애플리케이션 재시작 후 등록 목록이 JSON Registry에서 복원된다.
- Windows에서는 경로 비교 시 대소문자를 무시하고 Unix 계열에서는 구분한다.
- 손상된 Registry는 빈 목록으로 대체되지 않으며 사용자에게 별도 오류로 전달된다.
- 등록 해제는 Registry만 변경하고 실제 폴더와 파일을 보존한다.
- 파일 시스템 객체의 영구 식별, 다중 프로세스 동시 쓰기, 서비스 IPC, 암호화, 감시 및 접근 통제는 아직 제공하지 않는다.

## 검증

- `dotnet build Drm.slnx --no-restore`가 경고와 오류 없이 통과했다.
- 기존 라이프사이클 테스트 9개와 Workspace 테스트 14개가 통과했다.
- Workspace 테스트는 정상 등록, 보호 미활성, 중복·중첩 거부, 구조화된 경로 실패, 취소, 저장 실패 비공개, 재시작 복원, Registry 손상 감지, 등록 해제 시 폴더 보존, 루트·임시 위치 거부와 플랫폼 대소문자 규칙을 검증한다.

## 관련

- [[initial-drm-lifecycle-architecture]]
- [작업공간 등록 설계](../../docs/workspace-registration.md)

## 한국어 문자열 외부화

### 변경 사항

- 관련 소스, XAML과 Markdown을 엄격한 UTF-8로 검사했다. 현재 작업트리에서는 잘못된 UTF-8 바이트나 대체 문자로 손상된 사용자 문자열이 재현되지 않았다.
- XAML, `MainViewModel`, Folder Picker와 Workspace 사용자 오류의 한국어 문구를 `Drm.Desktop/Localization/Strings.resx`로 이동했다.
- `ILocalizationService`와 한국어 기준 `LocalizationService`를 추가하고 Desktop composition root에서 UI 구성 요소에 주입했다.
- `WorkspaceValidationResult`에서 `UserMessage`를 제거해 Domain·Application·Platform 계층이 `WorkspaceValidationCode`만 반환하도록 변경했다.
- `WorkspaceErrorLocalizer`에 모든 오류 코드의 명시적 리소스 키 mapping과 알 수 없는 코드의 일반 오류 fallback을 추가했다.
- `JsonWorkspaceRegistry`와 플랫폼 adapter에는 사용자 번역과 분리된 내부 진단 예외만 남겼다.

### 설계

오류 enum 이름과 리소스 키를 직접 결합하지 않는다. `Workspace.Validation`, `Workspace.Policy`, `Workspace.Storage` 영역을 포함한 의미 기반 키를 사용하므로 코드 식별자 변경이나 화면별 문구 확장 시 번역 키를 독립적으로 유지할 수 있다.

이 단계에서는 국제화 기반만 구성했으며, 영어 fallback과 지원 culture 카탈로그는 후속 변경에서 추가했다. 사용자 언어 선택 UI, 사용자 preferences, 시작 전 preference 적용 및 즉시 전환은 아직 포함하지 않는다.

### 검증

- 전체 Workspace 오류 코드가 비어 있지 않은 한국어 리소스에 mapping되는지 검증했다.
- 알 수 없는 오류 코드 fallback, 리소스 중복·빈 값, 한국어 기준 문자열, 사용자 결과의 문장 비저장과 관련 파일의 엄격한 UTF-8 decoding을 검증했다.
- Avalonia application XAML 리소스가 실제로 로드되는지 검증했다.
- 전체 빌드는 경고 0개, 오류 0개로 통과했고 기존 테스트 9개와 Workspace 테스트 21개가 모두 통과했다.

## 지원 culture 카탈로그와 영어 fallback

### 변경 사항

- 정상 한국어 값이 들어 있던 중립 `Strings.resx`를 영어 fallback과 기준 키 계약으로 전환하고, 동일한 키 집합의 `Strings.ko-KR.resx`를 추가했다.
- 번역자가 등록 해제와 보호 미활성의 의미를 혼동하지 않도록 영어 기준 리소스에 문맥 comment를 추가했다.
- `LocalizationService`에 명시적 culture 조회와 format 기능을 추가했다. 운영 호출은 시작 시 고정되는 `CurrentUICulture`를 유지하고, 테스트와 리소스 검증은 전역 culture 변경 없이 culture를 명시한다.
- `SupportedUiCultures`에 `en-US`, `ko-KR`와 영어 기본값을 중앙화했다.
- `UiCultureResolver`를 순수 정책으로 추가해 명시적 선택, 시스템 exact match, 언어 수준 match, 영어 fallback을 처리한다.
- 인스턴스형 `WorkspaceErrorLocalizer`를 정적 `WorkspaceMessageKeys`로 단순화해 오류 코드에서 안정적인 리소스 키로의 mapping만 담당하게 했다.

### 설계

중립 영어 리소스의 키 집합을 번역 계약으로 취급한다. 지원 culture는 중립 리소스와 정확히 같은 키를 제공해야 하며 fallback이 개발 중 누락을 가리지 못하도록 `.resx` XML을 직접 비교한다. 새 culture는 리소스 파일 추가, 정적 카탈로그 등록, 자동 검증, 실제 UI 레이아웃 확인의 순서로 도입한다.

실행 중 culture 변경 요구가 없으므로 `IUiCultureProvider`나 변경 이벤트는 도입하지 않았다. 사용자 preference 저장과 언어 선택 UI도 이번 변경에서 제외했다.

### 검증

- 영어·한국어 리소스의 키 집합 일치, 빈 값·중복·고아 키 부재와 UTF-8/XML 유효성을 검증했다.
- 모든 `WorkspaceValidationCode` mapping 키가 두 culture에 존재하는지 검증했다.
- 영어·한국어 명시 조회, culture별 일반 오류 fallback과 format 동작을 검증했다.
- format placeholder 번호 집합 일치와 각 culture의 실제 `string.Format` 성공을 검증했다.
- 지원 culture 카탈로그와 실제 culture 파일의 일치, resolver의 explicit/exact/language/default 경로를 검증했다.
- 영어 중립 fallback이 assembly metadata에도 `NeutralResourcesLanguage("en-US")`로 선언됐는지 검증했다.
- 전체 빌드는 경고 0개, 오류 0개로 통과했고 기존 테스트 9개와 Workspace 테스트 32개가 모두 통과했다.
