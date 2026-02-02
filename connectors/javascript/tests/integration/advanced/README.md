# Advanced Integration Tests

This directory contains advanced integration tests for the JavaScript connector.

## Test Files

### A01_WebSocketAdvanced.test.ts (7 tests)
Advanced WebSocket connection tests including:
- Connection success
- Authentication over WebSocket
- Echo request-response
- Push message reception
- Reconnection after disconnect
- Parallel request handling
- OnConnect event

### A02_LargePayload.test.ts (5 tests)
Large payload handling tests with LZ4 compression:
- Receiving 1MB payloads
- Data integrity validation
- Sequential large requests
- Sending large request payloads
- Parallel large payload requests

### A03_SendMethod.test.ts (7 tests)
Fire-and-forget send() method tests:
- Basic send() functionality
- Connection maintenance
- Mixing send() and request()
- Broadcast with push messages
- Error handling on disconnect
- Pre-authentication behavior
- Rapid fire send() calls

### A04_OnErrorEvent.test.ts (8 tests)
OnError event handling tests:
- Error on disconnected send()
- Error on disconnected request()
- Original request packet in error
- StageId information in error
- Error on disconnected authenticate()
- Multiple error handlers
- Exception handling in error handlers
- Normal disconnect without error

### A05_MultipleConnector.test.ts (7 tests)
Multiple connector instance tests:
- Simultaneous connections
- Independent authentication
- Simultaneous requests
- Independent disconnection
- Same stage connections
- Stress test with many connectors
- Independent event handling

### A06_EdgeCase.test.ts (13 tests)
Edge case and boundary condition tests:
- Connect without init
- Config validation
- Default config values
- Timeout configuration
- Invalid host/port handling
- Empty msgId handling
- Multiple disconnect calls
- Post-disposal operations
- StageId/StageType storage
- Very long strings
- Special character handling

## Total Test Count

- **Total Tests**: 47
- **Total Lines**: 1,318

## Running Tests

```bash
# Run all advanced tests
npm run test:integration -- advanced/

# Run specific test file
npm run test:integration -- advanced/A01_WebSocketAdvanced.test.ts

# Watch mode
npm run test:integration:watch -- advanced/
```

## Test Patterns

All tests follow the Given-When-Then pattern:
```typescript
test('A-XX-YY: Test description', async () => {
    // Given: Setup preconditions
    await testContext['createStageAndConnect']();
    
    // When: Execute action
    const result = await testContext['connector']!.request(packet);
    
    // Then: Verify expectations
    expect(result).toBeDefined();
});
```

## Dependencies

- BaseIntegrationTest helper class
- TestServerClient for stage creation
- Vitest test framework
- PlayHouse test server (must be running)

## Test Server

Tests require the PlayHouse test server to be running:
- HTTP Port: 8080 (configurable via TEST_SERVER_HTTP_PORT)
- WebSocket Port: 38001 (configurable via TEST_SERVER_WS_PORT)
- Host: localhost (configurable via TEST_SERVER_HOST)
