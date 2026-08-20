# DRM

## Workspace 파일 감시

Desktop 실행 중 등록된 Workspace의 기존 파일을 스캔하고 생성·변경·삭제·이름 변경을 관찰합니다. 감시 오류가 발생하면 전체 재스캔으로 현재 상태를 다시 확인합니다.

이 기능은 관찰 전용입니다. 파일을 암호화·삭제·이동하지 않으며 다른 프로그램의 접근을 차단하지 않습니다. `감시 중`은 DRM 보호가 활성화되었다는 의미가 아닙니다. 자세한 제한 사항은 [Workspace 파일 감시](docs/workspace-monitoring.md)를 참조하세요.

.NET 10 기반으로 DRM 생명주기와 보호 대상 작업공간을 안전하게 관리하기 위한 기반 프로젝트입니다. 현재 구현은 도메인, 애플리케이션, 플랫폼 및 UI의 책임을 분리하고 세션 상태 전이, 콘텐츠 open pipeline, 로컬 작업공간 등록 절차를 제공합니다.

> [!WARNING]
> 이 저장소는 아직 완성된 DRM 제품이 아닙니다. 실제 파일 암호화, 라이선스 서명 검증, 장치 바인딩, 키 보호, 변조 방지 및 커널 수준 접근 통제를 제공하지 않습니다. 현재 구현을 운영 환경의 콘텐츠 보호 수단으로 사용해서는 안 됩니다.

## 구현된 기능

### DRM 라이프사이클 기반

- 허용된 전이를 명시한 DRM 세션 상태 모델을 제공합니다.
- 환경 검증, 인증, 라이선스 획득, 정책 평가 및 보호 콘텐츠 활성화로 구성된 typed open pipeline을 제공합니다.
- generation을 사용하여 종료 중 늦게 완료되는 비동기 open 결과가 세션을 다시 활성화하지 못하도록 방지합니다.
- 애플리케이션 레지스트리에서 여러 세션의 수명과 조회를 관리합니다.
- 외부 구현을 교체할 수 있도록 application port와 infrastructure adapter를 분리했습니다.
- 개발 및 테스트 전용 managed 보호 콘텐츠 엔진을 제공합니다.
- 향후 네이티브 모듈 연동을 위한 버전 지정 C ABI 초안을 정의했습니다.

이 프로젝트는 기능 호출을 단순히 나열하는 방식이 아니라, DRM 세션이 생성되어 보호 콘텐츠를 활성화하고 최종적으로 리소스를 정리할 때까지의 전체 라이프사이클을 명시적으로 관리하는 구조를 기반으로 합니다.

```text
정상 라이프사이클

Created -> Opening -> Active <-> Suspended
   |          |          |          |
   +----------+----------+----------+
                         |
                         v
                      Closing -> Closed

예외 및 보안 상태

Opening -----> Faulted ----+
   |                       |
   +---------> Revoked ----+----> Closing -> Closed

Active  ----> Faulted -----+
   |                       |
   +---------> Revoked ----+

Suspended --> Faulted -----+
   |                       |
   +---------> Revoked ----+
```

보호 콘텐츠를 활성화하는 open pipeline도 라이프사이클의 일부로 관리합니다. 각 단계가 성공해야만 다음 단계로 이동하며, 정책이 명시적으로 허용하지 않으면 콘텐츠 엔진을 호출하지 않는 fail-closed 방식을 적용합니다.

```text
OpenSessionRequest
  -> Environment Validation
  -> AuthenticationRequest
  -> AuthenticatedPrincipal
  -> VerifiedLicense
  -> PolicyDecision
       |-- Denied  -> DrmAccessDeniedException
       `-- Allowed -> IProtectedContentSession -> Active
```

세션을 닫기 시작하면 generation과 cancellation token을 갱신합니다. 따라서 이전 open 작업이 늦게 완료되더라도 `Active` 상태로 되돌아갈 수 없으며, 늦게 생성된 보호 콘텐츠 리소스는 폐기합니다.

### 작업공간 등록

- Avalonia 기반 Desktop 관리 화면을 제공합니다.
- Desktop과 Policy Maker는 navy-charcoal 배경, 구분된 surface, 고대비 본문·보조 텍스트와 일관된 정보·경고·오류 색을 사용하는 다크 UI를 제공합니다.
- 로컬 폴더를 등록하고 목록을 조회하며, 폴더 위치 열기와 등록 해제를 지원합니다.
- 동일하거나 서로 포함되는 작업공간이 중복 등록되지 않도록 방지합니다.
- 시스템, 애플리케이션 설정, 임시, 네트워크, 이동식, 알려진 클라우드 동기화 및 symlink/reparse point 위치를 거부합니다.
- JSON Registry에서 스키마 버전을 확인하고 손상을 감지합니다.
- 임시 파일 기록, 디스크 flush 및 파일 교체를 사용하여 Registry를 갱신합니다.
- Desktop 사용자 문자열과 작업공간 오류 메시지를 영어 중립 `Strings.resx`와 한국어 `Strings.ko-KR.resx` 리소스로 관리합니다.
- Domain, Application 및 Platform 계층은 사용자 문장을 반환하지 않고 `WorkspaceValidationCode`만 전달하며, Desktop 계층에서 현재 UI 문화권에 맞는 문구로 변환합니다.

작업공간 등록은 파일 보호 활성화와 분리되어 있습니다. 등록된 작업공간의 초기 보호 상태는 `NotActivated`이며, 등록하거나 등록을 해제해도 기존 폴더와 파일을 암호화·변환·삭제·덮어쓰지 않습니다.

현재 지원 UI culture는 `en-US`와 `ko-KR`이며, 영어를 기본 fallback으로 사용합니다. 지원되는 시스템 culture는 exact match 또는 언어 수준 match로 결정할 수 있습니다. 사용자 언어 선택 UI, 선택 값 저장, 앱 시작 전 preference 적용 및 실행 중 언어 전환은 아직 구현하지 않았습니다.

### 개발용 Policy Maker

- 일반 DRM Desktop과 분리된 Avalonia `Drm.PolicyMaker` 실행 파일을 제공합니다.
- 보호 정책 Draft를 새로 만들거나 기존 JSON을 열어 편집·검증·미리보기·Save As 할 수 있습니다.
- 포함·제외 확장자, 신규·기존 파일 후보 지정, 최대 파일 크기 및 UTC 유효기간을 지원합니다.
- 유효기간은 달력과 시간 Picker로 입력하며, 편집 내용은 250ms debounce 후 JSON 미리보기에 자동 반영됩니다.
- 같은 `Drm.Policy` 라이브러리가 정규화, 구조화된 검증, capability 호환성, JSON 직렬화와 실행 snapshot 생성을 담당합니다.
- 같은 정책은 결정적인 JSON으로 저장하며 임시 파일을 다시 로드·검증한 후 대상 파일로 교체합니다.

Policy Maker가 만드는 결과는 `Draft` 상태의 unsigned development policy입니다. 전자서명, 관리자 인증, 승인, 중앙 배포, 회수 및 실제 파일 보호 집행은 제공하지 않습니다. 생성된 JSON을 운영 정책으로 신뢰해서는 안 됩니다.

### DRM Desktop 정책 검증

- DRM Desktop에서 Policy Maker가 만든 로컬 JSON을 선택해 크기·UTF-8·JSON·schema·capability·정책 값을 다시 검증할 수 있습니다.
- 검증에 성공한 문서는 불변 EffectiveProtectionPolicy snapshot으로 compile하고 정책 ID, version, 확장자, 최대 크기와 출처를 읽기 전용으로 표시합니다.
- Debug 빌드에서만 unsigned development Draft를 표시용으로 허용합니다. Release 빌드는 동일 문서를 Untrusted로 거부합니다.
- 정상적으로 불러온 inspection 정책은 작업공간에서 관찰한 `Existing`과 `Created` 항목을 평가하는 데만 사용합니다. 최근 관찰 UI에는 수집 상태, 평가 결과와 현지화된 사유를 표시합니다.
- 이 평가는 파일을 변경하거나 암호화하지 않으며, `Eligible`은 보호가 적용되었다는 의미가 아니라 보호 후보라는 의미만 나타냅니다.
- 정책 파일은 하나의 열린 stream에서 최대 1 MiB + 1 byte까지만 읽으며, 실패한 로드는 직전의 정상 snapshot을 교체하지 않습니다.

## 요구 사항

- .NET 10 SDK
- Windows, macOS 또는 Linux의 `dotnet` CLI 실행 환경
- Desktop UI를 실행할 수 있는 그래픽 환경

## 빌드 및 테스트

저장소 루트에서 다음 명령을 실행해야 합니다.

```powershell
dotnet build Drm.slnx
dotnet test Drm.slnx
```

현재 자동화 테스트는 DRM 라이프사이클, 작업공간 등록·영속성·경로 정책·파일 감시와 정책 작성·소비 경계를 검증합니다.

## Desktop 실행

```powershell
dotnet run --project src/Drm.Desktop/Drm.Desktop.csproj
```

작업공간 목록은 사용자별 로컬 애플리케이션 데이터 디렉터리의 `Drm/workspaces.json`에 저장됩니다. Windows의 기본 위치는 `%LOCALAPPDATA%\Drm\workspaces.json`입니다. 이 Registry에는 비밀번호, 인증 token 또는 암호화 key를 저장하지 않습니다.

창을 닫으면 Workspace monitor와 정책 inspection 작업을 비동기로 정리한 뒤 Desktop 프로세스가 종료됩니다. UI thread를 동기 대기로 막지 않으므로 정상 종료 후 dotnet run을 실행한 terminal prompt가 다시 표시됩니다.

## Policy Maker 실행

```powershell
dotnet run --project src/Drm.PolicyMaker/Drm.PolicyMaker.csproj
```

정책 계약과 현재 제한 사항은 [Policy Maker 및 정책 Draft](docs/policy-maker.md)를 참조하세요.

## 현재 제약 사항

- 작업공간은 이미 존재하며 읽기·쓰기가 가능한 로컬 고정 디스크의 일반 디렉터리만 지원합니다.
- 단일 사용자와 단일 관리 프로세스를 전제로 하며 다중 프로세스 Registry 동시 쓰기는 지원하지 않습니다.
- canonical path는 중복과 중첩 판정에 사용하지만 영구 파일 시스템 식별자가 아니므로, 폴더 이동이나 검증 직후 위치 교체를 완전히 방어하지 못합니다.
- 이전 관찰 이후에 정책을 불러와도 기존 파일을 자동으로 다시 평가하지 않습니다. `Modified`와 `Renamed`는 파일 나이를 알 수 없어 `Deferred`로 유지되며, `Created` metadata는 파일 안정성을 보장하지 않는 순간 snapshot입니다.
- 재시도, durable queue, 암호화와 접근 통제는 제공하지 않습니다.
- `Drm.Host`는 향후 서비스 composition root를 위한 자리이며 아직 보호 콘텐츠 재생이나 서비스 IPC를 제공하지 않습니다.

## 프로젝트 구성

```text
src/
  Drm.Domain/                 상태, 정책 및 불변 경계 타입
  Drm.Application/            세션과 작업공간 use case 오케스트레이션
  Drm.Infrastructure/         정책, 시간 및 JSON 영속성 adapter
  Drm.ManagedEngine/          개발·테스트용 보호 콘텐츠 엔진
  Drm.Platform.Abstractions/  플랫폼별 기능의 공통 경계
  Drm.Platform.Local/         로컬 파일 시스템 위치 검증과 실행 adapter
  Drm.Policy/                 정책 계약, 검증, 정규화, 직렬화와 실행 snapshot
  Drm.PolicyMaker/            Avalonia 기반 개발용 정책 Draft 작성 도구
  Drm.Desktop/                Avalonia 기반 작업공간 관리 UI
  Drm.Host/                   향후 Windows 서비스 진입점
native/include/               네이티브 연동용 C ABI 초안
tests/                        자동화 테스트
docs/                         현재 아키텍처와 기능 설계 문서
History/                      주요 변경의 배경과 설계 이력
```

## 관련 문서

- [아키텍처](docs/architecture.md)
- [아키텍처 실행 흐름](docs/architecture-flows.md)
- [작업공간 등록](docs/workspace-registration.md)
- [Workspace 파일 감시](docs/workspace-monitoring.md)
- [Policy Maker 및 정책 Draft](docs/policy-maker.md)
- [DRM 정책 소비 경계](docs/policy-consumption.md)
- [초기 DRM 라이프사이클 변경 이력](History/Architecture/initial-drm-lifecycle-architecture.md)
- [작업공간 등록 변경 이력](History/Workspace/workspace-registration.md)
- [Workspace 파일 감시 변경 이력](History/Workspace/workspace-file-monitoring.md)
- [Policy Maker 변경 이력](History/Policy/policy-maker-foundation.md)
- [DRM 정책 소비 경계 변경 이력](History/Policy/policy-consumption-boundary.md)
