#include "client_network.hpp"
#include "packet_codec.hpp"
#include "playhouse/types.hpp"
#include <atomic>
#include <iostream>
#include <map>

namespace playhouse {
namespace internal {

class ClientNetwork::Impl {
public:
    ConnectorConfig config_;
    TcpConnection connection_;
    RingBuffer receive_buffer_;
    std::atomic<uint16_t> msg_seq_counter_;
    std::map<uint16_t, std::function<void(Packet)>> pending_requests_;
    std::mutex pending_mutex_;
    std::mutex callback_mutex_;
    std::queue<std::function<void()>> callback_queue_;
    bool is_authenticated_;

    std::function<void(bool)> on_connect_;
    std::function<void(Packet)> on_receive_;
    std::function<void(int, std::string)> on_error_;
    std::function<void()> on_disconnect_;

    Impl(const ConnectorConfig& config)
        : config_(config)
        , receive_buffer_(config.receive_buffer_size)
        , msg_seq_counter_(1)
        , is_authenticated_(false)
    {
        connection_.SetReceiveCallback([this](const uint8_t* data, size_t size) {
            OnDataReceived(data, size);
        });

        connection_.SetDisconnectCallback([this]() {
            OnDisconnected();
        });
    }

    uint16_t GetNextMsgSeq() {
        uint16_t seq = msg_seq_counter_.fetch_add(1, std::memory_order_relaxed);
        if (seq == 0) {
            // Skip 0 as it's reserved for push messages
            seq = msg_seq_counter_.fetch_add(1, std::memory_order_relaxed);
        }
        return seq;
    }

    void OnDataReceived(const uint8_t* data, size_t size) {
        try {
            // Write to ring buffer
            receive_buffer_.Write(data, size);

            // Process complete packets
            ProcessPackets();
        } catch (const std::exception& e) {
            std::cerr << "Error processing received data: " << e.what() << std::endl;
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
            }
        }
    }

    void HandlePacket(Packet packet) {
        uint16_t msg_seq = packet.GetMsgSeq();

        // Check if this is a response to a pending request
        if (msg_seq > 0) {
            std::lock_guard<std::mutex> lock(pending_mutex_);
            auto it = pending_requests_.find(msg_seq);
            if (it != pending_requests_.end()) {
                // Use shared_ptr to allow copy-construction for std::function
                auto callback_ptr = std::make_shared<std::function<void(Packet)>>(std::move(it->second));
                auto packet_ptr = std::make_shared<Packet>(std::move(packet));
                pending_requests_.erase(it);

                // Queue callback for main thread
                QueueCallback([callback_ptr, packet_ptr]() {
                    (*callback_ptr)(std::move(*packet_ptr));
                });
                return;
            }
        }

        // Handle push message or unmatched response
        // Use shared_ptr to allow copy-construction for std::function
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

    void ClearPendingRequests() {
        std::lock_guard<std::mutex> lock(pending_mutex_);
        pending_requests_.clear();
    }
};

ClientNetwork::ClientNetwork(const ConnectorConfig& config)
    : impl_(std::make_unique<Impl>(config))
{}

ClientNetwork::~ClientNetwork() = default;

std::future<bool> ClientNetwork::ConnectAsync(const std::string& host, uint16_t port) {
    // Create a shared promise to coordinate between the returned future and callback
    auto promise = std::make_shared<std::promise<bool>>();
    auto future = promise->get_future();

    // Start single connection attempt
    auto connect_future = impl_->connection_.ConnectAsync(host, port);

    // Use async to wait for connection result and trigger callbacks
    std::thread([this, connect_future = std::move(connect_future), promise]() mutable {
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

        // Fulfill the promise
        promise->set_value(result);
    }).detach();

    return future;
}

void ClientNetwork::Disconnect() {
    impl_->ClearPendingRequests();
    impl_->connection_.Disconnect();
    impl_->is_authenticated_ = false;
}

bool ClientNetwork::IsConnected() const {
    return impl_->connection_.IsConnected();
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
    impl_->connection_.Send(encoded.data(), encoded.size());
}

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

    // Register callback
    {
        std::lock_guard<std::mutex> lock(impl_->pending_mutex_);
        impl_->pending_requests_[msg_seq] = std::move(callback);
    }

    // Send request
    Bytes encoded = PacketCodec::EncodeRequest(packet);
    impl_->connection_.Send(encoded.data(), encoded.size());
}

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

void ClientNetwork::Authenticate(Packet packet, std::function<void(Packet)> callback, int64_t stage_id) {
    Request(std::move(packet), [this, callback](Packet response) {
        if (response.GetErrorCode() == 0) {
            impl_->is_authenticated_ = true;
        }
        callback(std::move(response));
    }, stage_id);
}

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
