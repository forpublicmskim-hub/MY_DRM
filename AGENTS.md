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
