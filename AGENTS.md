# Repository Documentation Rules

These rules apply to every agent working in this repository.

## Documentation Roles

- `README.md`: what users can do now, including verified requirements, commands, supported scope, and limitations.
- `docs/`: the current system architecture, behavior, contracts, security boundaries, formats, and operational procedures.
- `History/`: meaningful changes: what changed, how, why, impact, decisions, and validation.

Write all three in Korean. Keep code identifiers, commands, API names, protocol names, and other technical terms in their original form when translation would reduce accuracy.

## Required Workflow

Before every commit:

1. Inspect `git status` and the actual diff.
2. Review the final staged diff for impacts to `README.md`, `docs/`, and `History/`.
3. Update every affected document from the implemented and verified result, not from the request or intended design.
4. Include required documentation updates in the same commit as the change.
5. Verify that commands, paths, identifiers, supported scope, and limitations match the current implementation.
6. Use the final staged diff to write the commit subject and body.

Avoid unrelated documentation churn. Clearly separate implemented behavior, current limitations, and future plans. Never present unverified or unimplemented behavior as complete.

Before pushing, inspect the cumulative outgoing commits and diff. If documentation is stale or missing, add and verify a correction commit before the push.

## README.md

Update `README.md` when a change affects user-visible functionality, usage, requirements, project structure, supported scope, security limitations, or build/test/run commands.

- Use consistent, polite Korean honorific prose.
- Document only commands and behavior that were actually verified.
- Do not edit the README for internal changes that do not affect users.

## docs/

Keep `docs/` as the current source of truth, not a change log. Update relevant documents when architecture, behavior, public contracts, state transitions, security boundaries, data formats, protocols, supported scope, recovery, or operations change.

- Create a new document only when the information will remain useful for future implementation or operation.
- Remove or revise stale descriptions when behavior changes.
- Distinguish current behavior from limitations and future plans.

## History/

Record new features and significant behavioral, architectural, security, performance, compatibility, bug-fix, or refactoring changes. Skip trivial formatting, typo, comment, and behavior-neutral changes.

- Organize entries by change purpose or feature, not by date.
- Update an existing document when work continues the same feature or design; create one for an independent change.
- Do not copy the raw Git diff.
- Review relevant History before changing an existing feature, while treating current code and requirements as authoritative.
- Use standard Obsidian-compatible Markdown and `[[Wiki Links]]` where useful.

Recommended organization:

```text
History/
|-- Encryption/
|   `-- protected-content-encryption-pipeline.md
|-- Session/
`-- Policy/
```

Use only the sections relevant to the change:

```markdown
# Change Title

## Summary

## Changes

## Design

## Impact

## Validation

## Related
```
