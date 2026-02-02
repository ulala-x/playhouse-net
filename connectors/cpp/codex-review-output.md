# Codex Code Review - C++ Connector

## Review Date: 2026-02-02
## Focus Areas: Code quality, memory management, thread safety, error handling, buffer management, socket lifecycle

---

## Critical/High Findings

### 1. Double Connect Call (Bug)
- **Location**: `src/client_network.cpp:159-170`
- **Issue**: `ClientNetwork::ConnectAsync` calls `TcpConnection::ConnectAsync` twice - once directly and once via `std::async`. This can start two IO threads and race two concurrent connect attempts.
- **Fix**: Return the single future from the first call and hook the completion to a queued callback without a second connect.

### 2. Use-After-Free in async_write (Critical Bug)
- **Location**: `src/tcp_connection.cpp:145`
- **Issue**: Passes a raw pointer to `asio::async_write` from a temporary buffer owned by the caller. Once `Send` returns, the buffer is freed and the async write uses invalid memory.
- **Fix**: Copy into a `shared_ptr<std::vector<uint8_t>>` (or a write-queue with shared ownership) and capture it in the completion handler.

### 3. Unfulfilled Promise (Bug)
- **Location**: `src/client_network.cpp:201` + `src/client_network.cpp:226`
- **Issue**: `RequestAsync` returns a future that never completes when not connected because `Request` only enqueues `OnError` and doesn't fulfill the promise.
- **Fix**: Set an exception or error result in `RequestAsync` when `Request` fails.

### 4. Potential Deadlock in Disconnect Callback
- **Location**: `src/tcp_connection.cpp:81`
- **Issue**: Calls `disconnect_callback_` while holding `mutex_`. If the callback calls `IsConnected()` or `Disconnect()`, deadlock occurs.
- **Fix**: Release the lock before invoking callbacks.

---

## Medium Findings

### 5. Main Thread Blocking
- **Location**: `src/client_network.cpp:163`
- **Issue**: Queues a callback that calls `future_ptr->get()` on the main thread. If `MainThreadAction()` runs before connect completes, it blocks the main thread.
- **Fix**: Complete the future on a background thread (or use a continuation in the IO thread) and only enqueue a ready-to-run callback.

### 6. Missing io_context Work Guard
- **Location**: `src/tcp_connection.cpp:38-48`
- **Issue**: Lacks an `io_context::work_guard` and never `restart()`. After `stop()`, reconnect attempts may never run.
- **Fix**: Add an `executor_work_guard`, call `io_context_.restart()` on reconnect, and avoid starting multiple IO threads.

### 7. Self-Join Risk
- **Location**: `src/tcp_connection.cpp:135`
- **Issue**: Can join the IO thread even if called from within that same IO thread (e.g., user calls `Disconnect()` inside callbacks).
- **Fix**: Guard against self-join or post the stop/close to the IO thread.

### 8. Silent Error Swallowing
- **Location**: `src/client_network.cpp:52`
- **Issue**: Swallows ring buffer overflow/packet errors by printing to `std::cerr` only.
- **Fix**: Bubble errors via `on_error_` and consider disconnecting on protocol violations to keep state consistent.

---

## Low/Quality/Best Practices Findings

### 9. OnConnect Ignores Success Parameter
- **Location**: `src/connector.cpp:38`
- **Issue**: Drops the `success` parameter and calls `OnConnect()` even on failure. This causes false-positive "connected" states.
- **Fix**: Pass the bool or call `OnError` on failure.

### 10. Ring Buffer Full Buffer Bug
- **Location**: `src/ring_buffer.cpp:137`
- **Issue**: `GetContiguousReadSize()` returns 0 when buffer is full and `head_ == tail_`, which is incorrect for a full buffer.
- **Fix**: Use `if (count_ == capacity_) return capacity_ - tail_;` to report readable data correctly.

### 11. Missing Request Timeout Handling
- **Location**: `src/client_network.cpp:189-197`
- **Issue**: Lacks request timeout handling even though `request_timeout_ms` exists in config.
- **Fix**: Add a timer per request (or a scheduler) to fail pending requests and clean `pending_requests_`.

### 12. Undocumented Thread Safety Assumptions
- **Location**: `src/client_network.cpp:52`
- **Issue**: Assumes single-threaded access to `receive_buffer_`. This invariant should be documented or protected.
- **Fix**: Document this invariant explicitly or protect with a mutex if callbacks can ever be called from multiple IO threads.

---

## Actionable Improvement Ideas

### Memory Management
- Implement a thread-safe write queue in `TcpConnection` that owns buffers until completion
- Serialize writes through `asio::strand` or a single writer

### Thread Safety
- Add `asio::strand` for socket ops and only touch `socket_` via `post` to the IO context
- Make callback setters thread-safe or enforce init-before-connect in API docs

### Error Handling
- Centralize error reporting (`OnError`) for decode errors, ring buffer overflows, and transport errors
- Consider propagating `asio::error_code` into `OnError`

### Buffer Management
- Use the ring buffer's zero-copy `GetWritePtr`/`Advance` to reduce copies
- Handle overflow by growing buffer or backpressuring (pause read)

### Socket Lifecycle
- Implement reconnect with exponential backoff
- On disconnect, cancel outstanding operations and clear pending requests with error completion
