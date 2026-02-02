# PlayHouse Unreal Connector Test Plan

## Goals

- Validate connector reliability before public use.
- Cover protocol correctness, request/response matching, and transport stability.
- Provide a path to test TCP, TLS, WS, and WSS.

## Test Layers

### 1) Core Logic Tests (PC/CI)

Target: engine-agnostic logic extracted from `connectors/cpp`.

Scope:
- Packet codec (encode/decode)
- Ring buffer framing
- Request map matching
- Timeout handling

Execution:
- UE Automation Tests or standalone C++ tests if core is isolated.

### 2) E2E Network Tests (PC/CI)

Target: full connector flow against a real server.

Scope:
- Connect/Disconnect
- Send/Request/Push
- Error propagation
- Timeout and reconnect

Transports:
- TCP
- TLS
- WS
- WSS

Execution:
- Start PlayHouse test server
- Run UE automation test or headless UE instance

### 3) Console Smoke Tests (Manual)

Target: platform validation on devkits.

Scope (minimal):
- Connect
- Request/Response
- Push receive
- TLS/WS/WSS connectivity

Execution:
- Dedicated test map with scripted connection
- Manual verification + log capture

## Minimum Required Scenarios

- TCP connect + request + response
- TCP push receive
- TLS connect + request + response
- WS connect + request + response
- WSS connect + request + response
- Timeout triggers callback
- Disconnect triggers callback

## Reliability Checks

- Reconnect behavior (if enabled)
- Large payload handling (near protocol MAX_BODY_SIZE)
- Rapid request bursts (100+)

## Reporting

- Each transport: pass/fail + log snippet
- Console smoke: device/model/OS version recorded
- Repro steps for failures
