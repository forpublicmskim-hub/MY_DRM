# 작업공간 등록

## 범위

첫 번째 작업공간 마일스톤은 사용자가 선택한 로컬 폴더를 DRM 보호 대상으로 등록하고 목록으로 관리한다. 등록은 파일 암호화나 접근 통제가 아니다. 등록된 모든 항목의 초기 보호 상태는 `NotActivated`이며, 기존 파일을 변환·삭제·덮어쓰지 않는다.

지원 범위는 이미 존재하고 읽기·쓰기 가능한 로컬 고정 디스크의 일반 디렉터리, 단일 사용자, 단일 관리 프로세스, 서로 중첩되지 않은 작업공간이다. 네트워크 공유, 이동식 디스크, 알려진 클라우드 동기화 위치, symlink/reparse point, 파일 시스템 루트, 운영체제·프로그램·애플리케이션 설정·임시 위치는 거부한다.

## 처리 흐름

`Drm.Desktop`은 Avalonia `StorageProvider`로 폴더를 선택할 뿐이며 경로를 승인하지 않는다. 선택된 경로는 다음 순서로 처리한다.

`Folder Picker -> IWorkspaceLocationResolver -> WorkspaceRegistrationPolicy -> WorkspaceService -> IWorkspaceRegistry`

- `IWorkspaceLocationResolver`는 실제 위치 존재 여부, 디렉터리 여부, 접근 가능성, 장치 유형과 플랫폼별 금지 위치를 검사하고 `WorkspaceLocation`을 만든다.
- `WorkspaceRegistrationPolicy`는 canonical path와 플랫폼 대소문자 규칙을 사용해 동일·부모·자식 작업공간을 거부한다.
- `WorkspaceService`는 등록 변경을 직렬화하고 저장이 성공한 뒤에만 성공 결과를 반환한다.
- `JsonWorkspaceRegistry`는 `schemaVersion`을 확인하며, 같은 디렉터리의 임시 파일에 기록하고 디스크 flush 후 대상 파일을 교체한다. 손상된 설정은 빈 목록으로 간주하지 않는다.

## 등록 해제

`UnregisterAsync`는 Registry 항목만 제거한다. 실제 폴더와 내부 파일에는 삭제, 복호화 또는 다른 변경을 수행하지 않는다. 실제 보호가 추가되면 보호된 파일 존재 여부와 반출 정책을 별도 유스케이스로 설계해야 한다.

## 보안 한계

현재 `CanonicalPath`는 중복·중첩 판정에 사용되지만 신뢰 가능한 영구 파일 시스템 식별자는 아니다. 폴더 이동, mount 변경 또는 검증 직후 위치 교체를 완전히 방어하지 못한다. 실제 암호화나 접근 통제를 수행하기 직전에는 위치를 다시 검증하고, 플랫폼별 `PlatformIdentity`와 지속 접근 참조를 구현해야 한다.

Registry는 비밀번호, 토큰 또는 키를 저장하지 않는다. 다중 프로세스 동시 쓰기와 서비스 소유 Registry는 다음 마일스톤 범위다.

## 사용자 문자열과 오류 코드

사용자 표시 문자열은 `Drm.Desktop/Localization`에서 관리한다. `Strings.resx`는 영어 중립 리소스이자 전체 키 계약이며, `Strings.ko-KR.resx`는 동일한 계약의 한국어 번역이다. 번역 키가 culture 리소스에 누락되면 영어 중립 리소스로 fallback한다. XAML, ViewModel과 Folder Picker는 `ILocalizationService`를 통해 문자열을 얻으며, Domain·Application·Platform 계층은 현재 UI culture나 번역 문구를 알지 않는다.

`WorkspaceValidationResult`는 `IsAllowed`와 `WorkspaceValidationCode`만 반환한다. 정적 `WorkspaceMessageKeys`가 명시적인 switch mapping으로 오류 코드를 의미 기반 리소스 키에 연결한다. enum 이름을 리소스 키로 직접 사용하지 않으며, 알 수 없는 코드는 `Common.UnexpectedError`로 fallback한다.

Registry와 파일 시스템 adapter의 예외 메시지는 사용자에게 직접 표시하지 않는 내부 진단 정보다. Desktop은 예외 형식 또는 오류 코드를 번역 가능한 일반 문구로 변환한다. 경로와 원본 OS 오류 같은 민감한 진단 정보는 현재 원격 telemetry로 전송하지 않는다.

`SupportedUiCultures`는 릴리스 지원 culture를 `en-US`, `ko-KR`로 중앙 관리하며 기본값은 `en-US`다. `UiCultureResolver`는 명시적 지원 culture, 시스템 culture exact match, 언어 수준 match, 영어 기본값 순서로 culture를 결정하는 순수 정책이다. `LocalizationService`는 실행 중에는 `CurrentUICulture`를 사용하고 테스트 및 검증에는 명시적 culture 조회와 format API를 제공한다.

새 언어를 추가할 때는 `Strings.{culture}.resx`를 추가하고 `SupportedUiCultures`에 등록한 다음 완전성 테스트를 통과시켜야 한다. 모든 지원 culture 파일은 영어 중립 리소스와 키 집합이 정확히 같아야 하며 빈 값, 고아 키와 format placeholder 불일치를 허용하지 않는다.

사용자 언어 설정, `settings.json`, 앱 시작 전 culture 적용, 언어 선택 UI와 실행 중 culture 변경은 아직 구현하지 않았다. 현재 지원 범위는 LTR 언어이며 RTL UI는 별도 검증 전까지 지원으로 간주하지 않는다.
