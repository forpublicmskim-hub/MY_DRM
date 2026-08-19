# DRM 아키텍처

## 현재 범위

이 저장소는 DRM 세션 생명주기, 로컬 Workspace 등록·관찰, 보호 정책 Draft 작성·검증, 보호 후보 수집·평가 기반을 제공합니다.

현재 구현은 운영 DRM이 아닙니다. 파일 암호화, 신뢰된 정책 서명, 라이선스 발행, 장치 binding, 변조 방지와 운영체제 수준 접근 통제를 제공하지 않습니다.

## 프로젝트 책임

- Drm.Domain: 세션 상태, Workspace 식별자와 언어 독립 도메인 모델
- Drm.Application: use case, Workspace orchestration, 정책 로딩, 후보 수집·평가 계약
- Drm.Policy: 정책 JSON 계약, 정규화, 검증, 직렬화와 runtime compile
- Drm.Platform.Abstractions: 운영체제별 기능의 공통 port
- Drm.Platform.Local: 로컬 경로, 파일 감시, 후보 metadata와 정책 파일 adapter
- Drm.Infrastructure: Registry, clock와 개발용 adapter
- Drm.ManagedEngine: 테스트·개발용 protected-content engine
- Drm.Desktop: Workspace, 관찰 결과와 정책 inspection UI
- Drm.PolicyMaker: unsigned 개발용 정책 Draft 작성 UI

## Workspace 등록과 관찰

Desktop은 로컬 일반 디렉터리를 Workspace로 등록하며 중복, 중첩, root, 금지 위치와 symlink/reparse point를 검증합니다. Registry 저장은 원자적 교체를 사용하고 등록 해제는 실제 폴더와 파일을 변경하지 않습니다.

Workspace monitor는 초기 inventory scan과 FileSystemWatcher 알림으로 최근 관찰 결과를 제공합니다. 알림 유실이나 overflow가 의심되면 재스캔합니다. Watching은 보호 완료를 뜻하지 않습니다.

FileSystemWatcher channel은 빠른 반응을 위한 힌트이며 실제 보호 작업의 source of truth로 사용하지 않습니다.

## 정책 작성과 소비

Policy Maker는 schema version 1의 unsigned Draft를 만듭니다. 정책 모델, validator, normalizer와 serializer는 Drm.Policy에 있으며 Desktop도 같은 계약으로 JSON을 다시 검증합니다.

성공한 정책은 PolicyId, PolicyVersion, canonical payload의 SHA-256 ContentDigest를 갖는 InspectedProtectionPolicy로 게시됩니다. digest는 로컬 snapshot 식별자일 뿐 전자서명이나 발행자 신뢰를 증명하지 않습니다.

## 보호 후보 수집과 평가

    WorkspaceMonitorEvent
      -> ProtectionCandidateCollector
      -> IProtectionCandidateMetadataReader
      -> ProtectionCandidate
      -> ProtectionCandidateEvaluator
      -> ProtectionCandidateDecision

collector는 Application 계층에서 관찰 의미와 metadata 결과를 결합합니다. Local reader는 안전한 상대 경로, reparse point, 파일 종류, 정규화 확장자, 크기와 version stamp를 검사합니다. metadata가 없거나 안전하지 않으면 후보를 만들지 않습니다.

evaluator는 파일 I/O 없는 순수 API이며 Eligible, Excluded, Deferred, PolicyInactive, Indeterminate를 구분합니다. 현재 collector와 evaluator는 runtime monitor, Desktop UI, queue에 연결되지 않았습니다.

## 보안 경계

1. 검증되지 않은 ProtectionPolicyDocument를 runtime 집행 입력으로 사용하지 않습니다.
2. unsigned inspection 정책을 enforceable 정책으로 변환하지 않습니다.
3. 정책 identity 불일치는 fail-closed인 PolicyInactive로 처리합니다.
4. metadata가 완전하지 않으면 ProtectionCandidate를 생성하지 않습니다.
5. Workspace 탈출과 reparse point 경로는 후보 수집 단계에서 거부합니다.
6. 이벤트 알림을 신뢰 가능한 파일 inventory로 간주하지 않습니다.
7. 경로·속성 검사에는 TOCTOU가 남으므로 실제 보호 직전에 handle identity, version, metadata와 정책을 재검증해야 합니다.

## 다음 구현 경계

1. 정책 provider 수명을 UI에서 composition root로 이동
2. 선택한 Workspace와 정책의 InspectionOnly binding
3. inventory/reconciliation 기반 dry-run coordinator와 결과 UI
4. 파일 안정화, metadata 재수집과 정책 재평가
5. 신뢰된 정책만 받는 영속 ProtectionJob
6. 원자적 보호 container 작성과 비정상 종료 복구

세부 정책 계약은 policy-consumption.md와 protection-candidate-evaluation.md를 참조합니다.
