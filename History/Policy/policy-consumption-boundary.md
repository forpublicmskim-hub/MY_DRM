# DRM 정책 소비 경계

## 요약

Policy Maker가 만든 로컬 JSON을 DRM Desktop에서 안전하게 읽고 공통 계약으로 다시 검증한 뒤, 불변 정책 snapshot과 신뢰·집행 상태를 읽기 전용으로 표시하는 소비 경계를 추가했습니다.

## 변경 사항

- IProtectionPolicySource와 source 읽기 상태를 추가했습니다.
- 로컬 정책 파일을 1 MiB + 1 byte로 제한해 읽고 엄격한 UTF-8을 적용했습니다.
- JSON·schema·capability·정책 값 검증과 EffectiveProtectionPolicy compile을 하나의 loader로 묶었습니다.
- InvalidDocument, Unsupported, Untrusted와 파일 source 실패를 구분했습니다.
- 성공한 snapshot만 현재 상태로 교체하고 실패 시 직전 snapshot을 유지하도록 했습니다.
- Desktop에 정책 선택, 상태, 검증 오류와 읽기 전용 요약 패널을 추가했습니다.
- 정책 상태 및 오류 문구를 영어·한국어 리소스에 추가했습니다.

## 설계

문서 유효성, 정책 신뢰와 실제 집행 여부를 분리했습니다. Debug 빌드만 unsigned development Draft를 표시용으로 허용하고 Release 빌드는 기본적으로 거부합니다. 성공한 Debug 로드도 작업공간이나 파일 보호에 적용하지 않습니다.

파일 source는 I/O 의미만 분류하며 Application loader가 정책 의미와 호환성, 신뢰 설정을 판정합니다. 취소는 구조화된 실패로 삼키지 않고 호출자에게 전파합니다.

## 영향

DRM Desktop과 Policy Maker가 실제 JSON 파일 경계를 통해 같은 정책을 해석하는지 검증할 수 있습니다. 정책 로드 실패는 Workspace 등록과 파일 감시 상태에 영향을 주지 않습니다.

## 검증

- Application loader의 정상 compile, unsigned 거부, invalid/unsupported 구분, 취소 전파와 이전 snapshot 유지를 자동화 테스트로 확인했습니다.
- 로컬 source의 정확한 최대 크기, 1 byte 초과, 잘못된 UTF-8, 파일 없음, 디렉터리 경로와 취소를 자동화 테스트로 확인했습니다.
- Desktop 정책 패널의 성공 표시, 집행 미적용 표시, 실패 시 요약 유지와 picker 취소를 자동화 테스트로 확인했습니다.
- 영어·한국어 리소스 키 집합과 모든 정책 상태·검증 코드 매핑을 자동화 테스트로 확인했습니다.
- 전체 솔루션 Release 빌드가 경고 0개, 오류 0개로 통과했고 전체 74개 테스트가 통과했습니다.

## 관련

- [DRM 정책 소비 경계](../../docs/policy-consumption.md)
- [Policy Maker 및 정책 Draft](../../docs/policy-maker.md)
- [[policy-maker-foundation]]
