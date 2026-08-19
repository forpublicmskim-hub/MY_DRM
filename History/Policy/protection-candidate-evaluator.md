# 보호 후보 수집 및 순수 평가 기반

## Summary

Workspace 관찰 이벤트를 완전한 파일 후보 snapshot으로 변환하는 collector와 해당 후보를 정책에 따라 판정하는 순수 evaluator 기반을 추가했습니다. 현재 기능은 inspection 전용이며 파일을 변경하지 않습니다.

## Changes

- ProtectionCandidateCollector와 구조화된 collection 결과를 추가했습니다.
- 파일 metadata 조회를 IProtectionCandidateMetadataReader port로 분리했습니다.
- Local adapter가 경로 탈출, rooted 경로와 reparse point를 거부하도록 구현했습니다.
- 상대 경로, 확장자, 파일 크기와 FileVersionStamp를 수집합니다.
- Existing과 Created만 확정적으로 분류하고 age를 알 수 없는 Modified·Renamed는 Deferred로 처리합니다.
- metadata 실패를 Ignored, Deferred, Rejected로 구분했습니다.
- ProtectionCandidateEvaluator는 외부 I/O 없이 정책 결과와 안정적인 reason code를 반환합니다.
- ProtectionCandidateInspectionProcessor가 collection 성공 결과와 현재 inspection 정책 snapshot을 연결합니다.
- 정책 없음은 policy.not-loaded로 보존하고 collection 실패에서는 정책 provider를 읽지 않습니다.
- inspection 결과 factory가 collection, decision과 후보 identity 조합의 불변식을 검사합니다.

## Design

Application collector는 파일 시스템을 알지 않고 Local adapter는 정책 판단을 알지 않습니다. metadata가 없거나 신뢰할 수 없으면 불완전한 ProtectionCandidate를 만들지 않습니다. processor는 정책 provider를 한 번만 읽어 같은 사건 처리 중 snapshot 교체의 영향을 받지 않으며 항상 Inspection mode를 사용합니다.

FileVersionStamp는 안정화 보조 정보일 뿐 보안 identity가 아닙니다. FileSystemWatcher 이벤트도 source of truth가 아니므로 실제 보호 작업 전에는 inventory reconciliation과 handle 기반 재검증이 필요합니다.

## Impact

Desktop UI와 monitor runtime 흐름은 변경되지 않았습니다. 정책이 나중에 로드될 때 기존 파일을 자동 재평가하는 pipeline, queue, 암호화, 키 처리와 Workspace 정책 binding도 아직 추가되지 않았습니다.

## Validation

- Application 단위 테스트 64개 통과
- Workspace 및 Local adapter Release 테스트 56개 통과
- Local platform 프로젝트 build 통과
- 정상 파일, 디렉터리, 확장자 없음, 대소문자 정규화, missing 파일, rooted 경로, 상위 경로 탈출과 취소 경로 검증
- 평가 완료, 정책 없음, collection 미완료, 정책 비활성, snapshot 단일 읽기와 결과 불변식 검증

## Related

- [[policy-consumption-boundary]]
- [[policy-maker-foundation]]
