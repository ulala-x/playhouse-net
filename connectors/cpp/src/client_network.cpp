#include "client_network.hpp"
#include "packet_codec.hpp"
#include "tcp_connection.hpp"
#include "ws_connection.hpp"
#include "playhouse/types.hpp"
#include <atomic>
#include <iostream>
#include <map>

// Additional ASIO headers for implementation
#if defined(ASIO_STANDALONE) || !defined(BOOST_ASIO_HPP)
    #include <asio/co_spawn.hpp>
    #include <asio/detached.hpp>
    #include <asio/use_awaitable.hpp>
    #include <asio/experimental/promise.hpp>
    #include <asio/steady_timer.hpp>
#else
    #include <boost/asio/co_spawn.hpp>
    #include <boost/asio/detached.hpp>
    #include <boost/asio/use_awaitable.hpp>
    #include <boost/asio/experimental/promise.hpp>
    #include <boost/asio/steady_timer.hpp>
#endif

#include <chrono>

namespace playhouse {
namespace internal {

class ClientNetwork::Impl {
public:
    ConnectorConfig config_;
    std::unique_ptr<IConnection> connection_;
    RingBuffer receive_buffer_;
    std::atomic<uint16_t> msg_seq_counter_;
    std::map<uint16_t, std::function<void(Packet)>> pending_requests_;

    // Coroutine support: store promises for awaitable requests
    std::map<uint16_t, std::shared_ptr<std::promise<Packet>>> pending_promises_;

    // Timeout tracking: map msg_seq to start time
    std::map<uint16_t, std::chrono::steady_clock::time_point> request_timestamps_;

    std::mutex pending_mutex_;
    std::mutex callback_mutex_;
    std::queue<std::function<void()>> callback_queue_;
    bool is_authenticated_;

    // Timeout checker thread and control
    std::thread timeout_thread_;
    std::atomic<bool> stop_timeout_checker_;

    std::function<void(bool)> on_connect_;
    std::function<void(Packet)> on_receive_;
    std::function<void(int, std::string)> on_error_;
    std::function<void()> on_disconnect_;

    Impl(const ConnectorConfig& config)
        : config_(config)
        , receive_buffer_(config.receive_buffer_size)
        , msg_seq_counter_(1)
        , is_authenticated_(false)
        , stop_timeout_checker_(false)
    {
        if (config_.use_websocket) {
            connection_ = std::make_unique<WsConnection>(config_.websocket_path);
        } else {
            connection_ = std::make_unique<TcpConnection>();
        }

        connection_->SetReceiveCallback([this](const uint8_t* data, size_t size) {
            OnDataReceived(data, size);
        });

        connection_->SetDisconnectCallback([this]() {
            OnDisconnected();
        });

        // Start timeout checker thread
        StartTimeoutChecker();
    }

    ~Impl() {
        StopTimeoutChecker();
    }

    void StartTimeoutChecker() {
        stop_timeout_checker_ = false;
        timeout_thread_ = std::thread([this]() {
            using namespace std::chrono_literals;
            while (!stop_timeout_checker_) {
                CheckRequestTimeouts();
                // Check every 100ms for timeouts
                std::this_thread::sleep_for(100ms);
            }
        });
    }

    void StopTimeoutChecker() {
        stop_timeout_checker_ = true;
        if (timeout_thread_.joinable()) {
            timeout_thread_.join();
        }
    }

    uint16_t GetNextMsgSeq() {
        uint16_t seq = msg_seq_counter_.fetch_add(1, std::memory_order_relaxed);
        if (seq == 0) {
            // Skip 0 as it's reserved for push messages
            seq = msg_seq_counter_.fetch_add(1, std::memory_order_relaxed);
        }
        return seq;
    }

    // Thread Safety: This method is called exclusively from the TcpConnection's
    // IO thread (ASIO's async_read_some callback). The receive_buffer_ is only
    // accessed from this single IO thread context, ensuring thread-safe access
    // without additional synchronization. If this invariant changes (e.g., multiple
    // IO threads), receive_buffer_ access must be protected with a mutex.
    void OnDataReceived(const uint8_t* data, size_t size) {
        try {
            // Write to ring buffer
            receive_buffer_.Write(data, size);

            // Process complete packets
            ProcessPackets();
        } catch (const std::exception& e) {
            std::cerr << "Error processing received data: " << e.what() << std::endl;

            // Notify error through callback
            if (on_error_) {
                QueueCallback([this, error_msg = std::string(e.what())]() {
                    on_error_(error_code::PROTOCOL_VIOLATION, error_msg);
                });
            }

            // Clear buffer and consider disconnecting on protocol violation
            receive_buffer_.Clear();
        }
    }

    void ProcessPackets() {
        while (true) {
            // Check if we have enough data for header
            if (receive_buffer_.GetCount() < 4) {
                break;
            }

            // Read content size (4 bytes, little-endian)
            uint8_t size_bytes[4];
            receive_buffer_.Peek(size_bytes, 4, 0);
            uint32_t content_size =
                static_cast<uint32_t>(size_bytes[0]) |
                (static_cast<uint32_t>(size_bytes[1]) << 8) |
                (static_cast<uint32_t>(size_bytes[2]) << 16) |
                (static_cast<uint32_t>(size_bytes[3]) << 24);

            // Check if content size is valid
            if (content_size > protocol::MAX_BODY_SIZE) {
                std::cerr << "Invalid content size: " << content_size << std::endl;

                // Notify error through callback
                if (on_error_) {
                    QueueCallback([this, content_size]() {
                        on_error_(error_code::PROTOCOL_VIOLATION,
                                  "Invalid content size: " + std::to_string(content_size));
                    });
                }

                receive_buffer_.Clear();
                break;
            }

            // Check if we have complete packet
            uint32_t total_size = 4 + content_size;
            if (receive_buffer_.GetCount() < total_size) {
                break;
            }

            // Read complete packet
            std::vector<uint8_t> packet_data(total_size);
            receive_buffer_.Read(packet_data.data(), total_size);

            // Decode packet
            try {
                Packet packet = PacketCodec::DecodeResponse(packet_data.data(), total_size);
                HandlePacket(std::move(packet));
            } catch (const std::exception& e) {
                std::cerr << "Error decoding packet: " << e.what() << std::endl;

                // Notify error through callback
                if (on_error_) {
                    QueueCallback([this, error_msg = std::string(e.what())]() {
                        on_error_(error_code::INVALID_RESPONSE, error_msg);
                    });
                }
            }
        }
    }

    void HandlePacket(Packet packet) {
        uint16_t msg_seq = packet.GetMsgSeq();

        // Check if this is a response to a pending request
        if (msg_seq > 0) {
            std::lock_guard<std::mutex> lock(pending_mutex_);

            // Remove timeout tracking
            request_timestamps_.erase(msg_seq);

            // Check for awaitable promise first
            auto promise_it = pending_promises_.find(msg_seq);
            if (promise_it != pending_promises_.end()) {
                auto promise = promise_it->second;
                pending_promises_.erase(promise_it);

                // Fulfill the promise (this will resume the coroutine)
                promise->set_value(std::move(packet));
                return;
            }

            // Check for callback-based request
            auto callback_it = pending_requests_.find(msg_seq);
            if (callback_it != pending_requests_.end()) {
                // Move callback and packet into shared_ptr for efficient sharing
                // This uses one allocation instead of two separate shared_ptrs
                auto state = std::make_shared<std::pair<std::function<void(Packet)>, Packet>>(
                    std::move(callback_it->second), std::move(packet));
                pending_requests_.erase(callback_it);

                // Queue callback with shared state - copyable lambda
                QueueCallback([state]() {
                    state->first(std::move(state->second));
                });
                return;
            }
        }

        // Handle push message or unmatched response
        // Use shared_ptr to make lambda copyable (required by std::function)
        auto packet_ptr = std::make_shared<Packet>(std::move(packet));
        QueueCallback([this, packet_ptr]() {
            if (on_receive_) {
                on_receive_(std::move(*packet_ptr));
            }
        });
    }

    void QueueCallback(std::function<void()> callback) {
        std::lock_guard<std::mutex> lock(callback_mutex_);
        callback_queue_.push(std::move(callback));
    }

    void OnDisconnected() {
        QueueCallback([this]() {
            if (on_disconnect_) {
                on_disconnect_();
            }
        });
    }

    void CheckRequestTimeouts() {
        std::lock_guard<std::mutex> lock(pending_mutex_);

        auto now = std::chrono::steady_clock::now();
        auto timeout_ms = std::chrono::milliseconds(config_.request_timeout_ms);

        // Collect timed out requests
        std::vector<uint16_t> timed_out_requests;

        for (const auto& [msg_seq, start_time] : request_timestamps_) {
            if (now - start_time >= timeout_ms) {
                timed_out_requests.push_back(msg_seq);
            }
        }

        // Process timed out requests
        for (uint16_t msg_seq : timed_out_requests) {
            request_timestamps_.erase(msg_seq);

            // Check for awaitable promise
            auto promise_it = pending_promises_.find(msg_seq);
            if (promise_it != pending_promises_.end()) {
                auto promise = promise_it->second;
                pending_promises_.erase(promise_it);

                try {
                    promise->set_exception(std::make_exception_ptr(
                        std::runtime_error("Request timeout")));
                } catch (...) {
                    // Promise may have already been set, ignore
                }

                // Notify error callback
                if (on_error_) {
                    QueueCallback([this, msg_seq]() {
                        on_error_(error_code::REQUEST_TIMEOUT,
                                  "Request timeout for msg_seq: " + std::to_string(msg_seq));
                    });
                }
                continue;
            }

            // Check for callback-based request
            auto callback_it = pending_requests_.find(msg_seq);
            if (callback_it != pending_requests_.end()) {
                pending_requests_.erase(callback_it);

                // Notify error callback
                if (on_error_) {
                    QueueCallback([this, msg_seq]() {
                        on_error_(error_code::REQUEST_TIMEOUT,
                                  "Request timeout for msg_seq: " + std::to_string(msg_seq));
                    });
                }
            }
        }
    }

    void ClearPendingRequests() {
        std::lock_guard<std::mutex> lock(pending_mutex_);

        // Cancel all pending promises with exception
        for (auto& [msg_seq, promise] : pending_promises_) {
            try {
                promise->set_exception(std::make_exception_ptr(
                    std::runtime_error("Connection closed")));
            } catch (...) {
                // Promise may have already been set, ignore
            }
        }
        pending_promises_.clear();

        pending_requests_.clear();
        request_timestamps_.clear();
    }
};

ClientNetwork::ClientNetwork(const ConnectorConfig& config)
    : impl_(std::make_unique<Impl>(config))
{}

ClientNetwork::~ClientNetwork() = default;

// C++20 Coroutine version
asio::awaitable<bool> ClientNetwork::Connect(const std::string& host, uint16_t port) {
    // Start single connection attempt
    auto connect_future = impl_->connection_->ConnectAsync(host, port);

    // Wait for connection result
    // Note: This is still using future.get() which blocks, but we're calling it directly
    // rather than in a detached thread. The proper fix would be to make TcpConnection
    // support ASIO's async operations natively with use_awaitable.
    bool result = false;
    try {
        result = connect_future.get();
    } catch (...) {
        result = false;
    }

    // Queue connection callback for main thread
    impl_->QueueCallback([this, result]() {
        if (impl_->on_connect_) {
            impl_->on_connect_(result);
        }
    });

    co_return result;
}

// Legacy std::future version (deprecated)
std::future<bool> ClientNetwork::ConnectAsync(const std::string& host, uint16_t port) {
    // Create a shared promise to coordinate between the returned future and callback
    auto promise = std::make_shared<std::promise<bool>>();
    auto future = promise->get_future();

    // Start single connection attempt
    auto connect_future = impl_->connection_->ConnectAsync(host, port);

    // Use std::async instead of detached thread to avoid use-after-free
    // The shared_ptr to ClientNetwork::Impl ensures the object stays alive
    // Note: We intentionally discard the future returned by std::async because
    // we communicate the result via the promise parameter. The async task will
    // complete independently and set the promise value.
    auto impl_ptr = impl_.get();
    (void)std::async(std::launch::async, [impl_ptr, connect_future = std::move(connect_future), promise]() mutable {
        bool result = false;
        try {
            result = connect_future.get();
        } catch (...) {
            result = false;
        }

        // Queue connection callback for main thread
        impl_ptr->QueueCallback([impl_ptr, result]() {
            if (impl_ptr->on_connect_) {
                impl_ptr->on_connect_(result);
            }
        });

        // Fulfill the promise
        promise->set_value(result);
    });

    return future;
}

void ClientNetwork::Disconnect() {
    impl_->ClearPendingRequests();
    impl_->connection_->Disconnect();
    impl_->is_authenticated_ = false;
}

bool ClientNetwork::IsConnected() const {
    return impl_->connection_->IsConnected();
}

bool ClientNetwork::IsAuthenticated() const {
    return impl_->is_authenticated_;
}

void ClientNetwork::Send(Packet packet, int64_t stage_id) {
    if (!IsConnected()) {
        throw std::runtime_error("Not connected");
    }

    packet.SetStageId(stage_id);
    packet.SetMsgSeq(0);  // Send is always msgSeq 0

    Bytes encoded = PacketCodec::EncodeRequest(packet);
    impl_->connection_->Send(encoded.data(), encoded.size());
}

// C++20 Coroutine version
asio::awaitable<Packet> ClientNetwork::Request(Packet packet, int64_t stage_id) {
    if (!IsConnected()) {
        throw std::runtime_error("Not connected");
    }

    uint16_t msg_seq = impl_->GetNextMsgSeq();
    packet.SetStageId(stage_id);
    packet.SetMsgSeq(msg_seq);

    // Create promise for awaitable
    auto promise = std::make_shared<std::promise<Packet>>();
    auto future = promise->get_future();

    // Register promise and timestamp
    {
        std::lock_guard<std::mutex> lock(impl_->pending_mutex_);
        impl_->pending_promises_[msg_seq] = promise;
        impl_->request_timestamps_[msg_seq] = std::chrono::steady_clock::now();
    }

    // Send request
    Bytes encoded = PacketCodec::EncodeRequest(packet);
    impl_->connection_->Send(encoded.data(), encoded.size());

    // Wait for response using non-blocking polling with ASIO timer
    // This avoids blocking the coroutine thread while waiting for the response.
    // The response will be delivered via HandlePacket() which sets the promise.
    auto executor = co_await asio::this_coro::executor;
    asio::steady_timer timer(executor);

    using namespace std::chrono_literals;
    constexpr auto timeout = 30s;
    constexpr auto poll_interval = 10ms;

    auto start_time = std::chrono::steady_clock::now();
    while (true) {
        // Check if response is ready
        auto status = future.wait_for(0ms);
        if (status == std::future_status::ready) {
            co_return future.get();
        }

        // Check for timeout
        auto elapsed = std::chrono::steady_clock::now() - start_time;
        if (elapsed >= timeout) {
            // Clean up pending promise
            std::lock_guard<std::mutex> lock(impl_->pending_mutex_);
            impl_->pending_promises_.erase(msg_seq);
            throw std::runtime_error("Request timeout");
        }

        // Sleep briefly using ASIO timer (non-blocking)
        timer.expires_after(poll_interval);
        co_await timer.async_wait(asio::use_awaitable);
    }
}

// Callback overload version
void ClientNetwork::Request(Packet packet, std::function<void(Packet)> callback, int64_t stage_id) {
    if (!IsConnected()) {
        if (impl_->on_error_) {
            impl_->QueueCallback([this]() {
                impl_->on_error_(error_code::CONNECTION_CLOSED, "Not connected");
            });
        }
        return;
    }

    uint16_t msg_seq = impl_->GetNextMsgSeq();
    packet.SetStageId(stage_id);
    packet.SetMsgSeq(msg_seq);

    // Register callback and timestamp
    {
        std::lock_guard<std::mutex> lock(impl_->pending_mutex_);
        impl_->pending_requests_[msg_seq] = std::move(callback);
        impl_->request_timestamps_[msg_seq] = std::chrono::steady_clock::now();
    }

    // Send request
    Bytes encoded = PacketCodec::EncodeRequest(packet);
    impl_->connection_->Send(encoded.data(), encoded.size());
}

// Legacy std::future version (deprecated)
std::future<Packet> ClientNetwork::RequestAsync(Packet packet, int64_t stage_id) {
    auto promise = std::make_shared<std::promise<Packet>>();
    auto future = promise->get_future();

    // Check connection status before calling Request
    // Request() returns early without calling callback when not connected
    if (!IsConnected()) {
        promise->set_exception(std::make_exception_ptr(
            std::runtime_error("Not connected")));
        return future;
    }

    Request(std::move(packet), [promise](Packet response) {
        promise->set_value(std::move(response));
    }, stage_id);

    return future;
}

// C++20 Coroutine version
asio::awaitable<Packet> ClientNetwork::Authenticate(Packet packet, int64_t stage_id) {
    Packet response = co_await Request(std::move(packet), stage_id);
    if (response.GetErrorCode() == 0) {
        impl_->is_authenticated_ = true;
    }
    co_return response;
}

// Callback overload version
void ClientNetwork::Authenticate(Packet packet, std::function<void(Packet)> callback, int64_t stage_id) {
    Request(std::move(packet), [this, callback](Packet response) {
        if (response.GetErrorCode() == 0) {
            impl_->is_authenticated_ = true;
        }
        callback(std::move(response));
    }, stage_id);
}

// Legacy std::future version (deprecated)
std::future<Packet> ClientNetwork::AuthenticateAsync(Packet packet, int64_t stage_id) {
    auto promise = std::make_shared<std::promise<Packet>>();
    auto future = promise->get_future();

    // Check connection status before calling Authenticate
    // Authenticate() calls Request() which returns early without calling callback when not connected
    if (!IsConnected()) {
        promise->set_exception(std::make_exception_ptr(
            std::runtime_error("Not connected")));
        return future;
    }

    Authenticate(std::move(packet), [promise](Packet response) {
        promise->set_value(std::move(response));
    }, stage_id);

    return future;
}

void ClientNetwork::MainThreadAction() {
    std::queue<std::function<void()>> callbacks;

    {
        std::lock_guard<std::mutex> lock(impl_->callback_mutex_);
        callbacks.swap(impl_->callback_queue_);
    }

    while (!callbacks.empty()) {
        try {
            callbacks.front()();
        } catch (const std::exception& e) {
            std::cerr << "Error in callback: " << e.what() << std::endl;
        }
        callbacks.pop();
    }
}

void ClientNetwork::SetOnConnect(std::function<void(bool)> callback) {
    impl_->on_connect_ = std::move(callback);
}

void ClientNetwork::SetOnReceive(std::function<void(Packet)> callback) {
    impl_->on_receive_ = std::move(callback);
}

void ClientNetwork::SetOnError(std::function<void(int, std::string)> callback) {
    impl_->on_error_ = std::move(callback);
}

void ClientNetwork::SetOnDisconnect(std::function<void()> callback) {
    impl_->on_disconnect_ = std::move(callback);
}

} // namespace internal
} // namespace playhouse
