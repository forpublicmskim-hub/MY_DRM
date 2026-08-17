# 개발용 Policy Maker 기반

## 요약

보호 정책 계약을 시험할 수 있는 공통 `Drm.Policy` 라이브러리와 별도 Avalonia `Drm.PolicyMaker` 프로그램을 추가했습니다. 결과는 서명되지 않은 개발용 Draft이며 실제 DRM 집행에는 연결하지 않았습니다.

## 변경 사항

- typed Draft, JSON document와 불변 실행 snapshot을 분리했습니다.
- 확장자·크기·기간·schema·capability의 구조화된 검증을 추가했습니다.
- unknown field, null, 과대 문서와 미지원 capability를 fail-closed로 거부합니다.
- 결정적 JSON과 임시 파일 재검증 기반 Save As를 구현했습니다.
- 새 정책 작성, 열기, 검증 오류, 읽기 전용 미리보기와 저장 UI를 추가했습니다.
- 의미 있는 수정 시 로컬 정책 version을 증가시킵니다.
- UTC 유효기간을 달력·시간 Picker로 입력하도록 변경했습니다.
- 편집 입력을 250ms debounce한 뒤 검증하고 JSON 미리보기를 자동 갱신합니다.
- 오류 입력에서는 마지막 유효 JSON을 유지합니다.
- CalendarDatePicker.SelectedDate의 실제 DateTime? 계약과 ViewModel 타입을 일치시켜 날짜 선택 시 발생하던 InvalidCastException을 수정했습니다.
- 입력 영역의 세로 scrollbar, 24시간제 시간 Picker, UTC badge와 줄바꿈 가능한 하단 명령·상태 영역을 정리했습니다.

## 설계

Policy Maker ViewModel은 JSON이나 정책 의미를 직접 구현하지 않고 공통 라이브러리를 호출합니다. JSON document를 직접 집행하지 않고 검증·정규화 후 `EffectiveProtectionPolicy`로 compile하는 경계를 마련했습니다.

Policy Maker에는 운영 서명 키와 발행 권한을 넣지 않았습니다. `documentStatus`는 `Draft`만 지원합니다.

## 영향

관리자는 별도 프로그램에서 로컬 정책 계약을 작성하고 round-trip을 검증할 수 있습니다. DRM Desktop과 Workspace 감시 동작은 아직 이 정책을 읽거나 적용하지 않습니다.

## 검증

- `dotnet build Drm.slnx -c Release --no-restore`가 경고 0개, 오류 0개로 통과했습니다.
- `Drm.Policy.Tests` 8개와 `Drm.PolicyMaker.Tests` 5개가 통과했습니다.
- 기존 Application 테스트 9개와 Workspace 테스트 36개를 포함해 전체 58개 테스트가 통과했습니다.
- Policy Maker Release 빌드가 경고 0개, 오류 0개로 통과했고 날짜 바인딩 회귀 테스트를 포함한 Policy Maker 테스트 6개가 통과했습니다.

## 관련 문서

- [Policy Maker 및 정책 Draft](../../docs/policy-maker.md)
- [[workspace-file-monitoring]]
