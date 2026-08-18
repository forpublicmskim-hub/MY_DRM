# DRM 정책 소비 경계

## 목적과 현재 범위

DRM Desktop은 사용자가 명시적으로 선택한 Policy Maker JSON을 한 번 읽고 공통 정책 계약으로 다시 검증합니다. 성공한 문서는 불변 EffectiveProtectionPolicy와 PolicySnapshotIdentity를 가진 InspectedProtectionPolicy로 게시합니다.

이 기능은 개발 단계의 읽기 전용 inspection입니다. 정책을 Workspace에 연결하거나 파일 보호 작업을 만들지 않으며 정책 파일 변경을 자동으로 감시하지 않습니다.

## 처리 흐름

    Drm.Desktop
      -> ProtectionPolicyPanelViewModel
      -> ProtectionPolicyInspectionService
      -> ProtectionPolicyLoader
      -> IProtectionPolicySource
           -> LocalFileProtectionPolicySource
      -> ProtectionPolicySerializer
      -> PolicyNormalizer.Compile
      -> InspectedProtectionPolicy

Desktop과 Policy Maker는 서로를 참조하지 않고 Drm.Policy의 JSON 계약, 검증, 정규화와 compile 로직만 공유합니다.

## 제한된 파일 읽기

LocalFileProtectionPolicySource는 하나의 열린 FileStream에서 최대 1 MiB + 1 byte까지만 읽습니다. 한도를 초과하면 전체 파일을 메모리에 적재하지 않고 TooLarge를 반환합니다. 잘못된 UTF-8 byte sequence는 대체 문자로 바꾸지 않고 InvalidEncoding으로 거부합니다.

source 결과는 NotFound, AccessDenied, TooLarge, InvalidEncoding, Unavailable로 구분합니다. 취소는 실패 결과로 바꾸지 않고 OperationCanceledException으로 전파합니다.

## 문서, 신뢰와 집행 상태

- 문서 검증: JSON, schema, capability와 정책 값이 유효한지 판단합니다.
- 신뢰 상태: 정책 발행자와 서명을 신뢰할 수 있는지 판단합니다.
- 집행 상태: 정책이 실제 보호 작업에 사용되는지 나타냅니다.

현재 정책 문서는 모두 unsigned Draft입니다. Debug 구성에서는 UnsignedDevelopmentDraft로 inspection할 수 있지만 Release 구성에서는 Untrusted로 거부합니다. Debug에서 로드한 정책도 실제 집행에는 사용할 수 없습니다.

InspectedProtectionPolicy는 dry-run 입력으로만 사용할 수 있습니다. EnforceableProtectionPolicy와 VerifiedPolicyIdentity는 public 생성자를 제공하지 않으며, 향후 신뢰 검증 경계만 이 타입을 만들 수 있습니다.

## Snapshot identity

PolicySnapshotIdentity는 PolicyId, PolicyVersion, ContentDigest를 가집니다.

ContentDigest는 검증된 문서를 결정적인 canonical serialization으로 정규화한 뒤 계산한 소문자 SHA-256 hex 값입니다. 따라서 같은 ID와 version을 가진 JSON의 의미 있는 내용이 달라지면 서로 다른 snapshot으로 식별됩니다.

이 digest는 현재 로컬 snapshot 식별과 감사 추적 준비를 위한 값이며 전자서명이나 발행자 신뢰를 증명하지 않습니다.

## Snapshot 게시와 실패 격리

ProtectionPolicyInspectionService는 ICurrentProtectionPolicyProvider를 구현합니다. 성공한 immutable snapshot만 Volatile.Write로 게시하고 읽는 쪽은 Volatile.Read를 사용합니다. 이후 로드가 실패해도 직전 정상 snapshot은 유지합니다.

현재 service의 수명은 Desktop 정책 panel에 연결되어 있습니다. 후보 coordinator와 공유하는 수명 소유권 이동은 실제 dry-run 연결 단계에서 composition root로 옮깁니다.

정책 로드 실패는 Workspace 등록, monitor 상태와 최근 관찰 결과에 영향을 주지 않습니다.

## 현재 제한

- 전자서명, issuer 인증과 인증서 체인을 검증하지 않습니다.
- 서버 정책 다운로드, 자동 갱신, rollback 방지와 회수를 제공하지 않습니다.
- 정책과 Workspace를 연결하거나 Registry에 binding을 저장하지 않습니다.
- 후보 evaluator는 monitor나 파일시스템에 연결되지 않았습니다.
- digest는 서명용 canonical JSON 규격이 아닙니다.
