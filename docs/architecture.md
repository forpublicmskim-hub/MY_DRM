# DRM architecture skeleton

## Runtime boundaries

`DrmApplication` owns multiple `DrmSession` instances. Each session serializes short state changes, while network, disk, and cryptographic work happens outside its state gate. An operation generation prevents late asynchronous completion from reviving a closing session.

The open flow is deliberately typed:

`AuthenticationRequest -> AuthenticatedPrincipal -> VerifiedLicense -> IProtectedContentSession`

Policy evaluation is a direct, fail-closed call before content activation. Audit/UI/telemetry integrations are observers and must never grant access.

## Projects

- `Drm.Domain`: states, transition table, policies, opaque handles, immutable boundary types.
- `Drm.Application`: session orchestration, typed open pipeline, ports, application session registry.
- `Drm.Infrastructure`: replaceable policy, clock, audit, persistence, server and recovery adapters.
- `Drm.ManagedEngine`: development-only protected-content engine.
- `Drm.Host`: future Windows service composition root. It intentionally does not enable playback yet.
- `native/include`: versioned C ABI draft. No exceptions, STL types, or key bytes cross the ABI.

## Next increments

1. Define the signed license envelope and threat model, then implement signature/device-binding verification.
2. Add durable operation records and startup reconciliation with idempotency keys.
3. Implement renew, suspend/resume, revoke and heartbeat commands as separate pipelines.
4. Add a native user-mode core and `SafeHandle`-based C# adapter.
5. Add an authenticated, versioned service-to-driver protocol before implementing the minifilter.

The minifilter is intentionally deferred: driver policy should remain minimal, and its protocol depends on the threat model and native service boundary.
