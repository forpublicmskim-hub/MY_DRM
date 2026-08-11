## Change History

When committing changes, analyze the actual changes (`git diff`) and document meaningful changes under `History/` as Markdown.

- Organize History by **change purpose or feature**, not by date.
- Document only meaningful changes such as new features, significant bug fixes, refactoring, architectural changes, security changes, performance changes, or behavioral changes. Skip trivial formatting or typo fixes.
- Update an existing document when changes continue the same feature or design. Create a new document for an independent feature or significant design change.
- Do not duplicate the raw Git diff. Document **what changed, how it changed, why it changed, its impact, and important design decisions**.
- Base documentation on the **actual implementation and diff**, not merely on the request or intended design.
- Write all History documents in **Korean**. Preserve code identifiers, commands, API names, and other technical terms in their original form when translating them would reduce accuracy.
- Use standard Markdown compatible with Obsidian. Link related History documents with `[[Wiki Links]]` when useful.
- Include the History document in the same commit as the changes it describes.
- Before modifying an existing feature, review relevant History documents when available, but treat the current code and requirements as the source of truth.

Recommended structure:

```text id="7fmk5q"
History/
├── Encryption/
│   └── protected-content-encryption-pipeline.md
├── Session/
└── Policy/
```

Use only the sections that are relevant:

```markdown id="wjh96x"
# Change Title

## Summary
Purpose and summary of the change.

## Changes
Key implementation changes.

## Design
Architectural/design changes and important decisions.

## Impact
Impact on behavior, compatibility, performance, security, etc.

## Validation
Tests and validation performed.

## Related
Related documents: [[...]]
```

Before committing, inspect `git status` and the actual diff. After including the History documentation, use the final staged diff to write both the commit subject and body.

## 문서 작성 언어

- `README.md`와 `docs/` 아래의 모든 Markdown 문서는 한국어로 작성한다.
- 코드 식별자, 명령어, API 이름, 프로토콜 이름 등 번역하면 정확성이 떨어지는 기술 용어는 원문 표기를 유지한다.
- 코드 동작이나 아키텍처를 변경할 때는 관련 한국어 문서도 함께 갱신하여 현재 구현과 일치하도록 유지한다.

## README 유지관리

- `README.md`는 `합니다`, `했습니다`, `해야 합니다`와 같은 정중한 높임체로 작성하며, 설명문과 목록의 문체를 일관되게 유지한다.
- 모든 커밋을 만들기 전에 최종 staged diff를 기준으로 `README.md`가 현재 구현과 일치하는지 검토한다.
- 변경으로 사용자 기능, 실행 방법, 요구 사항, 프로젝트 구성, 지원 범위 또는 보안 한계가 달라지면 `README.md`를 한국어로 갱신하고 같은 커밋에 포함한다.
- 내부 구현 변경처럼 README에 영향을 주지 않는 경우에는 불필요하게 문구를 수정하지 않는다.
- push 전에 원격으로 전송할 커밋 범위를 다시 확인하고, 해당 범위의 누적 변경이 `README.md`에 반영되었는지 검토한다. 누락이 있으면 push 전에 README 수정과 검증을 별도 커밋으로 추가한다.
- README의 빌드, 테스트 및 실행 명령은 실제로 유효한 명령만 기재하며, 검증하지 않은 동작을 완료된 기능처럼 설명하지 않는다.
