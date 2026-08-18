# 보호 후보 순수 평가기

## Summary

정책과 정규화된 파일 후보 snapshot을 입력받아 파일을 변경하지 않고 보호 적격성을 판정하는 순수 evaluator를 추가했습니다.

## Changes

- 후보 age와 discovery kind, metadata 및 평가 context 모델을 추가했습니다.
- Eligible, Excluded, Deferred, PolicyInactive, Indeterminate 결과를 분리했습니다.
- 안정적인 reason code와 정책 snapshot identity를 결정 결과에 포함했습니다.
- 정책 비활성과 만료를 일반 파일 제외와 구분했습니다.
- 제외 우선, 빈 포함 목록, 최대 크기와 metadata 부족 의미를 고정했습니다.
- unsigned inspection snapshot의 enforcement 사용을 차단했습니다.
- snapshot ID/version/digest 불일치를 fail-closed로 처리했습니다.

## Design

평가기에는 파일 I/O, 현재 시각, monitor와 queue 의존성이 없습니다. 파일 발견 원인과 정책상 New/Existing 분류를 분리하고 확장자 정규화는 향후 platform inspector가 담당합니다. FileSystemWatcher는 실제 작업 source가 아니라 reconciliation을 빠르게 시작하기 위한 힌트로만 사용할 예정입니다.

## Impact

현재 파일 감시와 Desktop 동작은 변경되지 않습니다. 평가기는 아직 외부 흐름에 연결되지 않으며 파일 보호나 queue 등록을 수행하지 않습니다.

## Validation

- 정책 활성·기간과 종료 경계
- New/Existing flag
- 제외 우선과 빈 포함 목록
- 정규화되지 않은 확장자와 잘못된 metadata
- 최대 크기 경계 및 크기 정보가 필요 없는 정책
- unsigned enforcement 차단
- 정책 identity 불일치
- canonical digest 안정성과 내용 변경 구분

## Related

- [[policy-consumption-boundary]]
- [[policy-maker-foundation]]
