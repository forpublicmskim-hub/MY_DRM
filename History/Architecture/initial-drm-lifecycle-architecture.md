# Initial DRM Lifecycle Architecture

## Summary

Established the first production-oriented skeleton for a .NET 10 DRM lifecycle. The change defines explicit domain and runtime boundaries for opening, tracking, and closing protected-content sessions without claiming to provide production cryptography, license verification, tamper resistance, or kernel enforcement.

## Changes

- Added a layered solution composed of domain, application, infrastructure, managed-engine, host, and test projects.
- Added typed domain models for authentication, principals, content, verified licenses, policy decisions, opaque handles, connectivity, and lifecycle states.
- Implemented an ordered open pipeline that validates the environment, authenticates the caller, acquires a verified license, evaluates policy, and only then activates protected content.
- Added `DrmSession` orchestration for opening and closing protected-content resources, plus `DrmApplication` as a concurrent session registry and lifetime owner.
- Added replaceable application ports for environment validation, authentication, licensing, policy evaluation, protected-content activation, auditing, and time.
- Added default infrastructure implementations for system time, deny-by-default license policy evaluation, and a no-op audit sink.
- Added a development-only managed protected-content engine that returns opaque session handles and rejects license/content mismatches.
- Drafted a versioned C ABI for native session open and close operations using fixed-width values, explicit buffer lengths, opaque handles, and no key material in the interface.
- Added architecture documentation and focused tests for lifecycle transition rules and policy denial before engine activation.

## Design

The implementation separates policy and orchestration from protected-content mechanics. `Drm.Domain` owns immutable boundary types and the allowed state-transition table. `Drm.Application` coordinates work exclusively through interfaces, allowing production authentication, licensing, persistence, audit, and native-engine implementations to replace the initial adapters without changing the session API.

The open path is intentionally ordered and fail-closed:

`AuthenticationRequest -> AuthenticatedPrincipal -> VerifiedLicense -> PolicyDecision -> IProtectedContentSession`

A denied policy decision raises `DrmAccessDeniedException` before the protected-content engine is invoked. Audit and telemetry are observers and do not participate in access grants.

`DrmSession` protects short state mutations with a semaphore but performs network, disk, and protected-content work outside that gate. A monotonically increasing operation generation and a lifetime cancellation token prevent a late open result from reactivating a session that has begun closing. Any stale protected-content result is disposed before cancellation is reported to the caller. Close detaches the protected resource under the gate, disposes it outside the gate, moves the session to `Closed`, records an audit event, and then surfaces cleanup failure.

The managed engine is only a development seam. The planned production boundary is a native implementation exposed through a stable C ABI and consumed through safe managed handles; raw key bytes are not part of the current boundary.

## Impact

- Establishes maintainable extension points for future license verification, recovery, renewal, revocation, native enforcement, and service/driver communication.
- Prevents invalid lifecycle transitions through an explicit allow-list and rejects content activation when policy denies access.
- Provides concurrency protection against late asynchronous completion during session shutdown.
- Keeps the host intentionally non-functional for playback until production adapters and composition are available.
- Introduces no compatibility guarantee for the draft native ABI beyond its explicit version and structure-size fields.
- Does not yet implement real cryptography, signed-license verification, durable recovery, tamper resistance, or kernel/minifilter enforcement.

## Validation

- `dotnet test Drm.slnx --no-restore` passes all 9 tests on .NET 10.
- `OpenPipelineTests.DeniedPolicyNeverActivatesContent` verifies that policy denial throws and never calls the protected-content engine.
- `SessionTransitionsTests` verifies representative allowed transitions and rejects unsafe transitions such as `Created -> Active`, `Closed -> Active`, and `Revoked -> Active`.
- The solution enables nullable reference types, recommended analyzers, and warnings-as-errors for all projects.

## Related

- [Architecture overview](../../docs/architecture.md)
