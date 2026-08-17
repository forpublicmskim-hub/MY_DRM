# 고가시성 다크 UI

## Summary

DRM Desktop과 Policy Maker의 검은색 위주 화면을 navy-charcoal 기반의 고가시성 다크 UI로 조정했습니다.

## Changes

- 두 앱에 동일한 의미 기반 background, surface, border, text, accent, warning, danger brush 계약을 추가했습니다.
- Desktop을 OS theme 추종 대신 명시적인 Dark theme로 고정해 두 앱의 표현을 일치시켰습니다.
- 목록, 정책 패널, 입력 패널과 footer에 분리된 surface와 테두리를 적용했습니다.
- 버튼의 기본, pointer over, pressed 상태와 Workspace 선택 상태를 구분했습니다.
- 보조 설명, 감시 상태, unsigned 경고, 검증 오류를 의미색으로 통일했습니다.
- Policy Maker XAML에 남아 있던 깨진 한국어 문구를 복원했습니다.
- Policy Maker의 별도 UTC 배지가 TimePicker 내부 필드를 가리던 레이아웃을 제거하고 시·분 입력에 충분한 폭을 배정했습니다.

## Design

화면별 hexadecimal 색상 대신 App*Brush resource를 사용합니다. 경고와 오류는 색상에만 의존하지 않고 문구와 패널 경계도 유지합니다. 현재 제품 범위에서는 실행 중 theme 전환보다 두 관리 도구의 일관성과 가독성을 우선합니다.

## Impact

기능과 저장 형식은 바뀌지 않습니다. Desktop과 Policy Maker의 화면 표현 및 컨트롤 상호작용 상태만 변경됩니다.

## Validation

- 두 앱의 Release XAML 빌드
- 각 앱의 필수 theme resource 계약 자동화 테스트
- 두 앱의 실제 화면 배치 및 색상 확인

## Related

- [[policy-maker-foundation]]
- [[workspace-registration]]
