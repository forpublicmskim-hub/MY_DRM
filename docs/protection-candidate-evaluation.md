# 보호 후보 평가 계약

## 현재 범위

ProtectionCandidateEvaluator는 검증된 정책 snapshot과 이미 정규화된 파일 후보 snapshot을 입력받아 외부 상태를 변경하지 않고 결정적인 결과를 반환하는 순수 static API입니다.

평가기에서는 파일 존재 확인, metadata 조회, 현재 시각 조회, 정책 로드, queue 등록과 암호화를 수행하지 않습니다. 현재 monitor, Desktop UI와 파일시스템에는 연결되어 있지 않습니다.

## 입력

ProtectionCandidate는 다음을 포함합니다.

- WorkspaceId, 상대 경로와 정규화된 확장자
- 정책 의미에 사용하는 ProtectionCandidateAge: Existing, New
- 진단용 ProtectionDiscoveryKind
- 디렉터리 여부와 선택적인 파일 크기

ProtectionEvaluationContext는 UTC 평가 시각과 Inspection 또는 Enforcement usage mode를 제공합니다. 파일 발견 정보와 평가 실행 시각을 분리하므로 같은 후보를 다른 시각에 재평가할 수 있습니다.

확장자는 metadata inspector가 소문자 점 접두 형식으로 정규화해야 합니다. 확장자가 없는 파일은 빈 문자열을 사용합니다. evaluator가 확장자를 경로에서 다시 추출하지 않습니다.

## 결과

- Eligible: 향후 보호 작업 후보가 될 수 있음
- Excluded: 정상적인 정책 조건으로 제외됨
- Deferred: 파일 안정화처럼 나중에 재평가해야 함. 현재 규칙에서는 생성하지 않음
- PolicyInactive: 정책을 현재 사용할 수 없음
- Indeterminate: metadata가 없거나 계약이 잘못되어 판단할 수 없음

결과에는 reason code, Workspace와 상대 경로, 평가 UTC 시각 및 PolicySnapshotIdentity가 포함됩니다.

## 결정 순서

| 순서 | 조건 | 결과 | reason code |
|---|---|---|---|
| 1 | identity의 ID/version/digest 형식 불일치 | PolicyInactive | policy.identity-invalid |
| 2 | 정책 비활성 | PolicyInactive | policy.disabled |
| 3 | 유효 시작 전 | PolicyInactive | policy.not-yet-valid |
| 4 | 유효 종료 이상 | PolicyInactive | policy.expired |
| 5 | 디렉터리 | Excluded | candidate.directory |
| 6 | New/Existing 정책 flag 비활성 | Excluded | candidate.age-disabled |
| 7 | 후보 identity, 경로, 확장자 또는 크기 형식 오류 | Indeterminate | candidate.metadata-invalid |
| 8 | 제외 확장자 | Excluded | candidate.extension-excluded |
| 9 | 포함 목록에 없음 | Excluded | candidate.extension-not-included |
| 10 | 크기 제한이 있으나 크기 없음 | Indeterminate | candidate.metadata-unavailable |
| 11 | 최대 크기 초과 | Excluded | candidate.file-too-large |
| 12 | 모든 조건 통과 | Eligible | protection.eligible |

유효 종료 시각은 exclusive 경계입니다. 파일 크기는 최대값과 같으면 허용합니다. 최대 크기 제한이 없으면 파일 크기가 없어도 평가할 수 있습니다.

schema version 1에서 빈 IncludedExtensions는 모든 파일을 제외한다는 의미입니다. 제외 목록은 포함 목록보다 우선합니다.

## 신뢰 경계

unsigned InspectedProtectionPolicy는 Inspection mode에서만 평가할 수 있습니다. 이를 Enforcement mode로 전달하면 PolicyInactive(policy.not-enforceable)를 반환합니다.

향후 신뢰 검증 component만 내부 생성자를 통해 VerifiedPolicyIdentity와 EnforceableProtectionPolicy를 만들 수 있습니다. 실제 ProtectionJob API는 이 enforceable 타입만 받아야 합니다.

## 후속 단계

아직 구현하지 않은 단계는 다음과 같습니다.

1. Workspace 내부 경로와 metadata를 안전하게 수집하는 platform inspector
2. current policy service 수명을 composition root로 이동
3. 명시적인 Workspace InspectionOnly binding
4. inventory/reconciliation 기반 dry-run coordinator와 UI
5. 파일 안정화 후 metadata 재수집 및 정책 재평가
6. 신뢰된 정책만 받는 영속 ProtectionJob

FileSystemWatcher 관찰 channel은 이벤트 유실 가능성이 있으므로 실제 작업의 source of truth로 사용하지 않습니다.
