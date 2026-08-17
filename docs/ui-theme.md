# Desktop UI 테마

Drm.Desktop과 Drm.PolicyMaker는 어두운 화면에서도 영역과 상태를 쉽게 구분할 수 있도록 동일한 의미 기반 팔레트 계약을 사용합니다.

## 팔레트 계약

- AppBackgroundBrush: 앱의 가장 아래 배경
- AppSurfaceBrush: 목록, 입력 영역과 정보 패널
- AppSurfaceElevatedBrush: 버튼과 강조된 컨트롤 표면
- AppBorderBrush: 패널과 입력 컨트롤 경계
- AppTextPrimaryBrush, AppTextSecondaryBrush: 본문과 보조 설명
- AppAccentBrush, AppAccentSurfaceBrush, AppAccentPressedBrush: 선택, 감시 상태, UTC 배지와 눌린 버튼
- AppWarningBrush, AppWarningSurfaceBrush: unsigned Draft와 미적용 상태
- AppDangerBrush, AppDangerSurfaceBrush: 검증 및 작업 오류

두 실행 파일은 같은 resource key 계약을 가지며 각 App.axaml에서 Dark theme variant를 명시합니다. 화면 XAML은 의미를 가진 brush resource를 참조하고 개별 색상값을 직접 선택하지 않습니다.

버튼은 기본, pointer over, pressed 상태를 서로 다른 표면색과 테두리로 구분합니다. 스크롤바는 surface와 border 색을 명시해 긴 입력 및 JSON 미리보기에서 위치를 식별할 수 있게 합니다. Desktop의 선택된 Workspace와 Policy Maker의 날짜 UTC 표시는 accent 계열을 사용합니다. 경고와 오류는 색상만으로 구분하지 않고 기존 문구 및 영역 구조를 함께 유지합니다.

현재 UI는 사용자 지정 테마와 Light theme 전환을 제공하지 않습니다.
