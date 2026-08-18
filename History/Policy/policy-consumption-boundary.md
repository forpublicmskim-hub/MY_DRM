# DRM 정책 소비 경계

## Summary

Policy Maker가 만든 로컬 JSON을 DRM Desktop에서 제한된 크기로 읽고 공통 계약으로 검증한 뒤 읽기 전용 정책 snapshot으로 표시하는 소비 경계를 구현했습니다.

## Changes

- 파일 source 오류를 NotFound, AccessDenied, TooLarge, InvalidEncoding, Unavailable로 구분했습니다.
- JSON, schema, capability와 정책 값 검증 및 EffectiveProtectionPolicy compile을 하나의 loader로 묶었습니다.
- unsigned Draft는 Debug inspection에서만 허용하고 Release에서는 Untrusted로 거부합니다.
- 성공한 snapshot만 현재 상태로 게시하며 실패 시 직전 정상 snapshot을 유지합니다.
- canonical 정책 payload의 SHA-256 digest를 포함하는 PolicySnapshotIdentity를 추가했습니다.
- 읽기 전용 ICurrentProtectionPolicyProvider와 inspection/enforcement 타입 경계를 추가했습니다.

## Design

문서 유효성, 정책 신뢰와 실제 집행 여부를 서로 다른 상태로 유지합니다. InspectedProtectionPolicy는 dry-run에 사용할 수 있지만 실제 작업 생성용 EnforceableProtectionPolicy로 변환할 수 없습니다. digest는 같은 ID/version의 서로 다른 내용을 구별하지만 신뢰나 전자서명을 증명하지 않습니다.

## Impact

Desktop과 Policy Maker가 실제 JSON 파일 경계에서 같은 정책 의미를 해석할 수 있습니다. 정책 로드 실패는 Workspace 등록과 파일 감시에 영향을 주지 않으며 현재도 어떠한 파일도 변경하지 않습니다.

## Validation

- 정상 compile, unsigned 거부, invalid/unsupported 구분과 취소 전파 테스트
- 최대 크기, UTF-8, 파일 없음과 접근 실패 source 테스트
- canonical digest 안정성과 내용 변경 구분 테스트
- Desktop 정책 요약 및 실패 격리 테스트

## Related

- [[policy-maker-foundation]]
- [[protection-candidate-evaluator]]
