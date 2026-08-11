# DRM

.NET 10을 기반으로 운영 환경을 고려해 설계한 DRM 생명주기 뼈대 프로젝트다. 현재 단계에서는 모듈의 책임과 보안 경계를 분리하고, 세션 상태 전이와 콘텐츠 열기 절차를 안전하게 오케스트레이션하는 기반을 제공한다.

## 현재 구현 범위

- 명시적인 DRM 세션 상태와 상태 전이표
- 환경 검증, 인증, 라이선스 획득, 정책 평가 및 콘텐츠 활성화로 구성된 타입 기반 파이프라인
- 여러 DRM 세션을 관리하는 애플리케이션 레지스트리
- 외부 구현을 교체할 수 있는 application port와 infrastructure adapter
- 개발 및 테스트용 managed 보호 콘텐츠 엔진
- 향후 네이티브 모듈 연동을 위한 버전 지정 C ABI 초안
- 핵심 상태 전이와 접근 거부 동작을 검증하는 단위 테스트

이 프로젝트는 아직 완성된 DRM 제품이 아니다. 실제 암호화, 라이선스 서명 검증, 장치 바인딩, 키 보호, 변조 방지 및 커널 수준 접근 통제를 제공하지 않는다. 현재 구현을 운영 환경의 콘텐츠 보호 수단으로 사용해서는 안 된다.

## 요구 사항

- .NET 10 SDK
- PowerShell 또는 `dotnet` CLI를 실행할 수 있는 터미널

## 빌드 및 테스트

저장소 루트에서 다음 명령을 실행한다.

```powershell
dotnet build Drm.slnx
dotnet test Drm.slnx
```

## 프로젝트 구성

```text
src/
  Drm.Domain/          상태, 정책 및 경계 타입
  Drm.Application/     세션과 파이프라인 오케스트레이션
  Drm.Infrastructure/  외부 시스템 adapter
  Drm.ManagedEngine/   개발·테스트용 콘텐츠 엔진
  Drm.Host/            향후 Windows 서비스 진입점
native/include/        네이티브 연동용 C ABI 초안
tests/                 자동화 테스트
docs/                  현재 아키텍처와 설계 문서
History/               주요 변경의 배경과 이력
```

각 프로젝트의 책임, 보안 경계 및 향후 구현 순서는 [아키텍처 문서](docs/architecture.md)를 참고한다.
