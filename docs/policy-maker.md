# Policy Maker 및 정책 Draft

## 목적과 신뢰 경계

`Drm.PolicyMaker`는 관리자가 로컬 보호 정책 Draft를 작성하는 별도 Avalonia 프로그램입니다. DRM 파일을 암호화하거나 세션을 제어하지 않습니다. 생성 파일은 서명되지 않은 `Draft`이며 운영 정책으로 신뢰할 수 없습니다.

```text
Drm.PolicyMaker
  → ProtectionPolicyDraft
  → Drm.Policy 검증·정규화
  → ProtectionPolicyDocument
  → 결정적 policy.json
  → 재로딩·재검증
  → EffectiveProtectionPolicy
```

Editor가 검증한 파일도 저장 후 변조될 수 있으므로 소비자는 `Drm.Policy`로 다시 파싱·검증·compile해야 합니다.

## 프로젝트 책임

- `Drm.Policy`: JSON document, Draft, validator, normalizer, serializer, atomic file store, `EffectiveProtectionPolicy`
- `Drm.PolicyMaker`: 입력, 오류 표시, 읽기 전용 JSON 미리보기, 파일 picker
- `Drm.Policy.Tests`: 계약·호환성·결정성·악성 입력·저장 round-trip
- `Drm.PolicyMaker.Tests`: ViewModel 저장·버전·검증·열기 흐름

정책 의미와 JSON 조립은 ViewModel에 두지 않습니다.

## schemaVersion 1

현재 지원 항목은 정책 ID·version·이름, `draft` 상태, 활성화 여부, 신규·기존 파일 후보 지정, 포함·제외 확장자, 최대 파일 크기, UTC 유효기간과 `requiredCapabilities`입니다.

지원 capability는 다음과 같습니다.

- `protection.extension-filter.v1`
- `protection.maximum-size.v1`
- `protection.validity-window.v1`

옵션에 필요한 capability가 누락되거나 관계없는 capability가 추가되거나 클라이언트가 지원하지 않으면 정책 전체를 거부합니다. 알 수 없는 JSON 필드, 주석, trailing comma, 정수 enum, 누락된 필수 생성자 값과 명시적 `null`도 거부합니다.

## 검증 및 제한

- 표시 이름: 최대 200자
- 각 확장자 목록: 최대 256개
- 확장자 길이: 최대 32자
- 정책 JSON: 최대 1 MiB, 최대 깊이 16
- 최대 보호 후보 파일 크기: 1 TiB 이하
- 포함·제외 충돌과 중복 확장자 거부
- `.drm`을 포함 확장자로 지정할 수 없음
- 종료 시간은 시작 시간보다 늦어야 함

확장자는 앞의 `.`을 보정하고 소문자로 변환한 뒤 중복 제거·정렬합니다. 시간은 UTC로 정규화합니다. pretty JSON은 향후 전자서명 canonical payload와 동일한 것으로 간주하지 않습니다.

## 저장과 버전

저장 시 같은 디렉터리에 임시 파일을 만들고 디스크 flush 후 공통 loader로 다시 읽어 동일한 결정적 JSON인지 확인합니다. 검증에 성공하면 대상 경로로 교체하며 임시 파일은 정리합니다.

- 새 정책은 `policyVersion = 1`입니다.
- 열린 정책의 의미 있는 내용이 바뀌면 다음 Save As에서 version을 1 증가시킵니다.
- 내용이 같으면 version을 유지합니다.
- 새로운 `policyId`로 작성하면 version은 다시 1입니다.

현재 Save As는 외부 파일 변경 충돌 검사나 백업 파일을 제공하지 않습니다.

## 현재 제외 범위

- DRM Desktop의 정책 적용과 파일 보호 후보 판정
- 파일 암호화 및 접근 통제
- 정책 서명과 issuer 신뢰
- 관리자 인증·권한·승인 workflow
- 중앙 배포·회수·최신 버전 보장
- 복잡한 조건식과 동적 UI plugin

운영 단계에서는 Draft를 별도 publisher에 제출하고 publisher가 검증·승인·버전 부여·서명한 문서만 DRM 클라이언트가 신뢰하도록 확장해야 합니다.
