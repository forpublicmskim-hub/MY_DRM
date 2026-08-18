# DRM 아키텍처

## 현재 범위

이 저장소는 DRM 세션 생명주기, 로컬 Workspace 등록과 관찰, 보호 정책 Draft 작성과 읽기 전용 검증, 순수 보호 후보 평가의 기반을 제공합니다.

현재 구현은 실제 운영 DRM이 아닙니다. 파일 암호화, 신뢰된 정책 서명, 라이선스 발행, 장치 binding, 변조 방지와 운영체제 수준 접근 통제를 제공하지 않습니다.

## 프로젝트 책임

- Drm.Domain: 세션 상태, Workspace 식별자와 언어 독립적인 도메인 모델
- Drm.Application: 세션 use case, Workspace orchestration, 정책 로딩과 후보 평가
- Drm.Policy: 정책 JSON 계약, 정규화, 검증, 직렬화와 runtime compile
- Drm.Platform.Abstractions: 운영체제별 기능의 공통 port
- Drm.Platform.Local: 로컬 경로, 파일 감시와 제한된 정책 파일 source
- Drm.Infrastructure: Registry, clock와 개발용 adapter
- Drm.ManagedEngine: 테스트와 개발용 protected-content engine
- Drm.Desktop: Workspace, 관찰 결과와 정책 inspection UI
- Drm.PolicyMaker: unsigned 개발용 정책 Draft 작성 UI
- Drm.Host: 향후 service composition root를 위한 자리
- native/include: 향후 native core 연동용 versioned C ABI 초안

## 세션 생명주기

DrmSession은 허용된 상태 전이만 수행합니다.

    Created -> Opening -> Active <-> Suspended
       |          |          |          |
       +----------+----------+----------+
                             |
                             v
                          Closing -> Closed

오류와 철회는 Faulted 또는 Revoked를 거쳐 Closing으로 이동합니다. 콘텐츠 open pipeline은 구체적인 typed input/output을 사용하며 환경 검증, 인증, 라이선스와 정책 평가가 모두 성공한 뒤에만 protected-content engine을 호출합니다. generation과 cancellation으로 늦게 완료된 작업이 종료된 세션을 재활성화하지 못하게 합니다.

## Workspace 등록과 관찰

Desktop은 로컬 일반 디렉터리를 Workspace로 등록하며 경로 중복, 중첩, root, symlink/reparse point와 금지 위치를 검사합니다. Registry 저장은 원자적 교체를 사용하고 등록 해제는 실제 폴더와 파일을 변경하지 않습니다.

Workspace monitor는 초기 inventory scan과 FileSystemWatcher 알림을 사용해 최근 관찰 결과를 UI에 표시합니다. 알림 유실이나 overflow가 의심되면 재스캔합니다. 이 monitor는 관찰 전용이며 보호 완료를 의미하지 않습니다.

FileSystemWatcher channel은 UI 반응을 위한 힌트이므로 향후 실제 보호 작업의 source of truth로 사용하지 않습니다. 실제 집행 전에는 현재 inventory와 reconciliation 경로가 필요합니다.

## 정책 작성과 소비

Policy Maker는 schema version 1의 unsigned Draft를 만듭니다. 정책 모델, validator, normalizer와 serializer는 Drm.Policy에 있으며 Desktop도 같은 계약으로 JSON을 다시 검증합니다.

LocalFileProtectionPolicySource는 최대 1 MiB + 1 byte만 읽고 올바른 UTF-8만 허용합니다. ProtectionPolicyLoader는 문서 검증과 compile을 수행하고 Debug inspection에서만 unsigned Draft를 허용합니다.

성공한 정책은 다음 identity를 가진 InspectedProtectionPolicy로 게시합니다.

    PolicyId
    PolicyVersion
    SHA-256 ContentDigest

digest는 canonical 정책 payload의 로컬 snapshot 식별자이며 전자서명이나 발행자 신뢰를 증명하지 않습니다. EnforceableProtectionPolicy는 public 생성자가 없고 향후 신뢰 검증 경계만 만들 수 있습니다.

## 보호 후보 평가

ProtectionCandidateEvaluator는 파일 I/O가 없는 순수 static API입니다.

    InspectedProtectionPolicy 또는 EnforceableProtectionPolicy
      + ProtectionCandidate
      + ProtectionEvaluationContext
      -> ProtectionCandidateDecision

결과는 Eligible, Excluded, Deferred, PolicyInactive, Indeterminate를 구분합니다. 정책 비활성이나 만료는 정상적인 확장자 제외와 섞지 않습니다. 결정에는 reason code, Workspace, 상대 경로, 평가 시각과 정책 snapshot identity가 포함됩니다.

unsigned inspection 정책은 Inspection mode에서만 평가할 수 있습니다. evaluator는 아직 Workspace monitor, Desktop UI, 파일 inspector와 queue에 연결되지 않았습니다.

## 보안 경계

1. 검증되지 않은 ProtectionPolicyDocument를 runtime 집행 입력으로 사용하지 않습니다.
2. unsigned inspection 정책을 enforceable 정책으로 변환하지 않습니다.
3. 정책 identity 불일치는 fail-closed인 PolicyInactive로 처리합니다.
4. 후보 evaluator는 외부 상태와 현재 시각을 직접 조회하지 않습니다.
5. 이벤트 알림을 신뢰 가능한 파일 inventory로 간주하지 않습니다.
6. 실제 파일 보호 직전에는 안정화된 metadata와 적용 정책을 다시 검증해야 합니다.
7. UI와 telemetry 실패가 보안 허가로 이어지지 않게 핵심 결정 경로와 부수 효과를 분리합니다.

## 다음 구현 경계

1. Workspace 탈출과 reparse point를 방지하는 LocalProtectionCandidateInspector
2. 정책 provider 수명을 UI에서 composition root로 이동
3. 선택한 Workspace와 정책의 InspectionOnly binding
4. inventory/reconciliation 기반 dry-run coordinator와 결과 UI
5. 파일 안정화, metadata 재수집과 정책 재평가
6. 신뢰된 정책만 받는 영속 ProtectionJob
7. 원자적 보호 container 작성과 비정상 종료 복구

자세한 정책 계약은 policy-consumption.md와 protection-candidate-evaluation.md를 참조합니다.
