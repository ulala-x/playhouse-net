# 2026-01-29 ServerType/ServiceId Refactoring Notes

## Goals
- Reduce duplication around ServerType/ServiceId handling.
- Improve naming consistency (ServerId vs NID) and clarify legacy identifiers.
- Tighten structure between discovery components and senders.

## Changes
- Abstractions
  - Removed deprecated `ServiceType` enum (use `ServerType` only).
  - Added `ServiceIdDefaults.Default` for shared default service group value.
  - Added `ServerOptionValidator` to centralize ServerType/ServerId/BindEndpoint validation.
  - Added `IServerInfoCenter.Update(...)` to formalize discovery refresh behavior.
- Core senders & dispatchers
  - `XSender` refactored to share header creation and request flow; removed duplicate send helpers.
  - `XStageSender` now holds a typed `BaseStage`, de-duplicates timer creation, and simplifies cancellation.
  - `XActorSender` uses a cached `StageSender` reference and clearer internal field naming.
  - `ApiDispatcher`/`PlayDispatcher` use `serverId` naming instead of `nid` in constructors/fields.
- Bootstrap & options
  - `PlayServerOption`/`ApiServerOption` default ServiceId now uses `ServiceIdDefaults.Default`.
  - Shared validation logic applied via `ServerOptionValidator`.
  - PlayServer bootstrap example updated to `ServerType`.
- Runtime/ServerMesh
  - `CommunicatorOption` default ServiceId now uses `ServiceIdDefaults.Default` and shared validation.
  - `ServerConfig.BindEndpoint` is now the primary property; `BindAddress` is marked obsolete.
  - `ServiceIds` marked obsolete to reflect legacy semantics.
  - `XServerInfoCenter.Update` now returns `IReadOnlyList<ServerChange>` and shares server filtering logic.
  - `ServerAddressResolver` depends on `IServerInfoCenter` and clarifies refresh loop naming.

## Compatibility Notes
- `ServiceType` enum removal is a breaking change for external callers.
- `ServiceIds` is now obsolete; prefer `ServiceIdDefaults.Default` or explicit group IDs.
- `BindAddress` is now obsolete; prefer `BindEndpoint`.

## Tests
- Not run in this refactor.
