# Codex Code Review - C# Connector

Review Date: 2026-02-02
Reviewed by: OpenAI Codex v0.93.0 (gpt-5.2-codex)

## Findings (most critical first)

### Critical

1. **Late response incorrectly treated as a push**: if a response arrives after timeout/removal, it falls through to `ReceiveCallback` instead of being dropped. This can surface "ghost" push events to user code and also masks timeouts. Fix: when `MsgSeq > 0` and no pending entry exists, dispose and ignore (or log), don't route to push.
   - Location: `src/PlayHouse.Connector/Internal/ClientNetwork.cs:432`

2. **`RequestAsync` can leak pending requests and never complete when send fails**: If `SendAsync` throws, the method exits before timeout registration, leaving the entry in `_pendingRequests` and the returned task stuck. Fix: wrap send in try/catch, remove pending, dispose, and `TrySetException`.
   - Location: `src/PlayHouse.Connector/Internal/ClientNetwork.cs:308`

3. **WebSocket receive buffer hard-limits packet size to 64KB** even though protocol allows up to 10MB. Large packets will throw in `WriteBytes` and disconnect. Fix: grow the ring buffer dynamically or bound max packet size to `ReceiveBufferSize` and validate early (reject with error).
   - Location: `src/PlayHouse.Connector/Network/WebSocketConnection.cs:28`, `src/PlayHouse.Connector/Network/WebSocketConnection.cs:191`

4. **ClientNetwork disposal leaves in-flight requests and queued packets undisposed**: `DisposeAsync()` only disposes the connection; it doesn't clear `_pendingRequests` nor drain `_packetQueue`, so pooled buffers can leak and awaiters can hang. Fix: call `DisconnectAsync()`, `ClearPendingRequests()`, and drain/dispose queued `ParsedPacket`s.
   - Location: `src/PlayHouse.Connector/Internal/ClientNetwork.cs:530`

5. **WebSocket parse path can leak ArrayPool buffers on parse errors**: In the non-compressed path, `payloadBuffer` is rented and only returned if parsing completes; if `PeekBytes` throws, the buffer is leaked. Fix: wrap in try/finally and return buffer on exceptions.
   - Location: `src/PlayHouse.Connector/Network/WebSocketConnection.cs:354`

### High/Medium Severity

6. **Response timeout race can re-route responses to push handlers**: `OnPacketReceived` checks `TryGetValue`, then calls `ProcessPacket`, which may fail `TryRemove` (timeout already removed), and then routes to push handler. Fix: in `ProcessPacket`, if `MsgSeq > 0` and `TryRemove` fails, dispose+ignore.
   - Location: `src/PlayHouse.Connector/Internal/ClientNetwork.cs:404`, `src/PlayHouse.Connector/Internal/ClientNetwork.cs:432`

7. **Missing protocol bounds checks in TCP and WebSocket parsing**: no validation that `contentSize >= headerTotalSize` or that `msgIdLen` keeps `offset` within `contentSize`. Corrupted packets can produce negative payload sizes or out-of-range reads. Fix: validate header sizes before reading payload and drop/close if invalid.
   - Location: `src/PlayHouse.Connector/Network/TcpConnection.cs:191`, `src/PlayHouse.Connector/Network/TcpConnection.cs:210`, `src/PlayHouse.Connector/Network/WebSocketConnection.cs:287`

8. **LZ4 decompression doesn't validate `originalSize` or cap output**: A malformed packet could allocate huge memory. Fix: enforce a max decompressed size (ideally config-based), and verify output size matches `originalSize`.
   - Location: `src/PlayHouse.Connector/Network/TcpConnection.cs:222`, `src/PlayHouse.Connector/Network/WebSocketConnection.cs:336`

### Thread Safety & Sync

9. **`_isAuthenticated` is written on the network thread and read on other threads without synchronization**; stale reads are possible. Fix: mark as `volatile` or use `Volatile.Read/Write` or `Interlocked.Exchange`.
   - Location: `src/PlayHouse.Connector/Internal/ClientNetwork.cs:30`, `src/PlayHouse.Connector/Internal/ClientNetwork.cs:440`

10. **`_debugMode` is set on caller thread and read on main thread without barriers**. Same fix as above if you want strict correctness.
    - Location: `src/PlayHouse.Connector/Internal/ClientNetwork.cs:31`, `src/PlayHouse.Connector/Internal/ClientNetwork.cs:140`

### Disposal & Error Handling

11. **`ClientNetwork` doesn't detach event handlers on dispose unless `CleanupConnectionAsync` runs**. If you skip `DisconnectAsync`, events can still be attached. Fix: reuse `CleanupConnectionAsync` inside `DisposeAsync` and null out.
    - Location: `src/PlayHouse.Connector/Internal/ClientNetwork.cs:530`

12. **`Connector.Connect` and `ConnectAsync` use `_clientNetwork!` without guarding `Init()`**. Consider throwing a clearer exception if not initialized.
    - Location: `src/PlayHouse.Connector/Connector.cs:83`

### Memory/Span Usage

13. **`PacketBuffer` and `RingBuffer` return pooled buffers without clearing**; if sensitive data is possible, consider `ArrayPool.Return(buffer, clearArray:true)`. This is a tradeoff but worth noting.
    - Location: `src/PlayHouse.Connector/Infrastructure/Buffers/PacketBuffer.cs:414`, `src/PlayHouse.Connector/Infrastructure/Buffers/RingBuffer.cs:299`

### Async Patterns

14. **`Request` uses `SendRequestAsync` fire-and-forget**. If `_connection.SendAsync` throws synchronously, the exception is observed only inside the task. Consider attaching a continuation to log or expose errors via callback (you already handle errors there).
    - Location: `src/PlayHouse.Connector/Internal/ClientNetwork.cs:238`

## Suggested Next Steps

1. Decide how to handle late responses (drop vs. error) and implement in `ProcessPacket`.
2. Fix `RequestAsync` send-failure cleanup.
3. Add protocol bounds checks and decompression limits.
4. Make WebSocket receive buffer strategy consistent with TCP path (max size or dynamic growth).
