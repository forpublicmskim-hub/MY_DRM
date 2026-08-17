# DRM 정책 소비 경계

## 목적과 현재 범위

DRM Desktop은 Drm.PolicyMaker가 만든 로컬 정책 JSON을 사용자가 명시적으로 선택했을 때 한 번 읽고, 공통 정책 계약으로 다시 검증하여 읽기 전용 요약을 표시합니다. 이 기능은 생성기와 소비자 사이의 파일 계약을 확인하기 위한 개발 단계 기능입니다.

불러온 정책은 작업공간에 연결되지 않으며 파일 감시, 보호 후보 판정, 암호화, 세션 접근 정책에 사용되지 않습니다. 정책 파일 변경도 자동으로 감시하거나 다시 불러오지 않습니다.

## 계층과 데이터 흐름

    Drm.Desktop
      -> ProtectionPolicyPanelViewModel
      -> ProtectionPolicyInspectionService
      -> ProtectionPolicyLoader
      -> IProtectionPolicySource
           -> LocalFileProtectionPolicySource
      -> ProtectionPolicySerializer
      -> PolicyNormalizer.Compile
      -> immutable ProtectionPolicySnapshot

Drm.Desktop은 Drm.PolicyMaker를 참조하지 않습니다. 두 실행 프로그램은 Drm.Policy의 JSON 계약·검증·compile 로직만 공유합니다.

source는 파일 시스템 실패만 분류하고, Application loader는 문서 검증·호환성·신뢰 판단을 담당합니다. 소비자에게 성공 결과로 노출되는 정책은 검증되지 않은 ProtectionPolicyDocument가 아니라 EffectiveProtectionPolicy입니다.

## 제한된 파일 읽기

LocalFileProtectionPolicySource는 하나의 FileStream handle을 열고 최대 1 MiB + 1 byte까지만 읽습니다. 1 MiB를 초과하면 전체 파일을 메모리에 적재하지 않고 TooLarge를 반환합니다. UTF-8은 잘못된 byte sequence를 대체 문자로 바꾸지 않고 InvalidEncoding으로 거부합니다.

다음 source 상태를 구분합니다.

- NotFound
- AccessDenied
- TooLarge
- InvalidEncoding
- Unavailable

취소는 실패 결과로 변환하지 않고 OperationCanceledException으로 전파합니다.

## 문서·신뢰·집행 상태

다음 의미를 하나의 성공 상태로 합치지 않습니다.

- 문서 검증: JSON, schema, capability와 정책 값이 유효한지 여부
- 신뢰 상태: 정책 발행자와 서명을 신뢰할 수 있는지 여부
- 집행 상태: 정책이 실제 작업공간이나 보호 처리에 사용되는지 여부

현재 문서는 모두 unsigned Draft입니다. Debug 빌드는 이를 UnsignedDevelopmentDraft로 표시용 로드할 수 있습니다. Release 빌드는 PolicyTrustOptions.Production을 사용하여 Untrusted로 거부합니다. Debug에서 검증에 성공해도 집행 상태는 항상 NotApplied입니다.

## 결과 분류

- InvalidDocument: JSON, 필수 값, capability 누락·불일치 또는 정책 값 오류
- Unsupported: 현재 클라이언트가 지원하지 않는 schemaVersion 또는 capability
- Untrusted: 문서는 유효하지만 현재 신뢰 설정에서 허용되지 않음
- NotFound, AccessDenied, TooLarge, Unavailable: source 읽기 실패

Unsupported는 정책을 수정해야 한다는 의미가 아니라 호환되는 DRM 클라이언트가 필요할 수 있음을 뜻합니다.

## Snapshot 수명과 실패 격리

성공한 로드는 파일 내용과 독립적인 불변 ProtectionPolicySnapshot을 만듭니다. 원본 파일을 나중에 변경해도 메모리 snapshot은 자동으로 바뀌지 않습니다. 이후 로드가 실패하면 ProtectionPolicyInspectionService.Current는 직전 정상 snapshot을 유지합니다.

정책 로드 흐름은 WorkspaceMonitorManager를 호출하지 않습니다. 따라서 정책 파일이 없거나 잘못되어도 등록된 Workspace, 감시 상태와 최근 관찰 결과에는 영향을 주지 않습니다.

## 현재 제한 사항

- 전자서명, issuer 신뢰와 인증서 체인을 검증하지 않습니다.
- 서버 정책 다운로드, 자동 갱신, rollback 방지와 정책 회수를 제공하지 않습니다.
- 정책과 WorkspaceId를 연결하거나 Registry에 저장하지 않습니다.
- 정책 오류의 전체 path와 arguments를 UI에 상세 표시하지 않고 현지화된 공통 원인만 표시합니다.
- 로컬 파일의 외부 변경 충돌이나 파일 시스템 수준의 강한 snapshot 보장은 제공하지 않습니다.
