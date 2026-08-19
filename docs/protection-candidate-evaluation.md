# 보호 후보 수집과 평가 계약

## 현재 범위

보호 후보 흐름은 파일을 변경하지 않는 inspection 전용 기반입니다.

    WorkspaceMonitorEvent
      -> ProtectionCandidateCollector
      -> IProtectionCandidateMetadataReader
      -> ProtectionCandidate
      -> ProtectionCandidateEvaluator
      -> ProtectionCandidateDecision

현재 collector와 evaluator는 runtime monitor에 연결되지 않았으며, queue 등록·키 처리·암호화를 수행하지 않습니다.

## 후보 수집 경계

ProtectionCandidateCollector는 Application 계층에서 monitor 사건을 정책 평가용 입력으로 변환합니다. 파일 시스템 API는 직접 호출하지 않고 IProtectionCandidateMetadataReader에 위임합니다.

| 관찰 | monitor 상태 | 결과 |
|---|---|---|
| Existing | 초기 scan | Existing + InitialInventory |
| Existing | Rescanning | Existing + Reconciliation |
| Created | Watching | New + Created |
| Created | Rescanning | New + Reconciliation |
| Deleted | 모든 상태 | Ignored |
| Modified, Renamed | 모든 상태 | Deferred(candidate.collection.age-unknown) |

Modified와 Renamed만으로는 정책의 New·Existing 의미를 확정할 수 없으므로 임의 분류하지 않습니다. 향후 권위 있는 inventory 또는 age tracker가 추가된 뒤 재평가해야 합니다.

Workspace ID가 event와 observation에 모두 일치하지 않으면 Rejected입니다. 상태 변경과 reconciliation 신호는 파일 후보가 아니므로 metadata reader를 호출하지 않습니다.

## metadata 계약

LocalProtectionCandidateMetadataReader는 안전하게 정규화한 Workspace 상대 경로, 마지막 확장자, 파일·디렉터리 구분, 파일 크기와 FileVersionStamp를 수집합니다.

| metadata 상태 | collection 결과 | 기본 의미 |
|---|---|---|
| Available | Collected | 완전한 후보 생성 |
| NotFound | Ignored | 관찰 후 대상이 사라짐 |
| AccessDenied | Deferred | 제한된 재시도 대상 |
| Unstable | Deferred | 안정화 후 재수집 대상 |
| Unavailable | Deferred | 일시적인 I/O 실패 가능 |
| UnsafePath | Rejected | Workspace 탈출 또는 잘못된 경로 |
| SymbolicLinkNotSupported | Rejected | reparse point 경로 |

Available 결과에는 metadata가 반드시 있고, 나머지 상태에는 metadata가 없도록 factory가 불변식을 보장합니다. 취소는 실패 상태로 바꾸지 않고 OperationCanceledException으로 전파합니다.

## Local 경로 안전성

Local reader는 rooted 입력과 상위 경로 탈출을 거부하고 canonical Workspace root 기준의 상대 경로를 다시 계산합니다. root부터 최종 항목까지 각 구성 요소의 FileAttributes.ReparsePoint를 검사합니다.

이 검사는 symbolic link·junction 위험을 줄이지만 검사와 실제 사용 사이의 TOCTOU를 제거하지 않습니다. 실제 보호 단계에서는 안전한 OS handle을 연 뒤 identity와 version을 다시 검증해야 합니다.

## 순수 평가

ProtectionCandidateEvaluator는 검증된 정책 snapshot과 정규화된 후보만 입력받는 순수 API입니다. 결과는 Eligible, Excluded, Deferred, PolicyInactive, Indeterminate를 구분합니다.

unsigned InspectedProtectionPolicy는 Inspection mode에서만 평가할 수 있습니다. 실제 ProtectionJob은 향후 신뢰 검증 경계가 만든 EnforceableProtectionPolicy만 받아야 합니다.

## version stamp 제한

FileVersionStamp는 파일 전체 digest가 아니며 파일 identity를 증명하지 않습니다. 수집 뒤 변경 가능성을 빠르게 발견하기 위한 비교 표식입니다. 실제 내용 처리 전에는 파일 안정화, metadata 재수집과 현재 정책 재평가가 모두 필요합니다.
