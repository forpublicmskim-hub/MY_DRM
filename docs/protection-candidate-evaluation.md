# 보호 후보 수집과 평가 계약

## 현재 범위

보호 후보 흐름은 파일을 변경하지 않는 inspection 전용 기반입니다.

    WorkspaceMonitorEvent
      -> ProtectionInspectionPipeline
         -> ProtectionCandidateInspectionProcessor
         -> ProtectionCandidateCollector
         -> IProtectionCandidateMetadataReader
         -> ICurrentProtectionPolicyProvider
         -> ProtectionCandidateEvaluator
      -> Desktop integrated result

`ProtectionInspectionPipeline`은 `WorkspaceMonitorManager` stream의 유일한 consumer입니다. pipeline은 활성 Workspace snapshot을 소유하고 monitor event를 순서대로 `ProtectionCandidateInspectionProcessor`에 전달한 뒤, monitor 정보와 inspection 결과를 결합한 integrated result를 Desktop에 전달합니다. 개별 event에서 발생한 예상하지 못한 실패는 `ProcessingFailed` 결과로 격리하므로 이후 event 처리는 계속됩니다.

manager와 pipeline의 channel은 기록된 출력을 조용히 폐기하지 않습니다. 로컬 channel이 포화되면 해당 출력을 버리는 대신 reconciliation을 요청하여 권위 있는 inventory를 다시 맞춥니다. 이 흐름은 inspection 전용이므로 queue 등록·키 처리·암호화를 수행하지 않습니다.

## inspection processor

ProtectionCandidateInspectionProcessor는 collection 결과가 Collected일 때만 현재 inspection 정책 snapshot을 한 번 읽고 평가합니다.

- 정책이 있으면 항상 PolicyUsageMode.Inspection으로 평가합니다.
- 정책이 없으면 후보를 버린 것으로 숨기지 않고 policy.not-loaded를 남깁니다.
- Ignored, Deferred, Rejected collection 결과에서는 정책 provider를 읽지 않습니다.
- IClock.UtcNow를 평가 context에 전달하며 processor가 시스템 시각을 직접 조회하지 않습니다.

ProtectionCandidateInspectionResult는 별도 상태 enum을 중복 저장하지 않습니다. Decision 존재 여부로 평가 완료를 판단하고, 평가하지 않은 경우 SkipReasonCode에 collection reason 또는 policy.not-loaded를 보존합니다. factory는 Collected 후보와 Decision의 Workspace·상대 경로가 일치하도록 불변식을 검사합니다.

정책이 나중에 로드돼도 `policy.not-loaded` 결과가 자동 재평가되지는 않습니다. 현재 pipeline은 정책 load를 계기로 기존 파일의 전체 Workspace 재평가 scan을 시작하지 않습니다.

현재 구현에는 파일 안정성 확인, 자동 retry, durable queue와 암호화가 없습니다. `Deferred`와 local saturation의 reconciliation 요청은 재검사가 필요하다는 사실을 보존하지만, 안정화 대기나 내구성 있는 작업 재실행을 제공하지는 않습니다.

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
