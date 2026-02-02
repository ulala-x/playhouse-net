#include "tcp_connection.hpp"
#include "playhouse/types.hpp"
#include <iostream>

#ifdef _WIN32
#define ASIO_STANDALONE
#endif

#include <asio.hpp>

namespace playhouse {
namespace internal {

class TcpConnection::Impl {
public:
    asio::io_context io_context_;
    asio::executor_work_guard<asio::io_context::executor_type> work_guard_;
    asio::ip::tcp::socket socket_;
    asio::ip::tcp::resolver resolver_;
    bool is_connected_;
    std::function<void(const uint8_t*, size_t)> receive_callback_;
    std::function<void()> disconnect_callback_;
    std::vector<uint8_t> receive_buffer_;
    std::thread io_thread_;
    std::mutex mutex_;
    std::thread::id io_thread_id_;

    Impl()
        : io_context_()
        , work_guard_(asio::make_work_guard(io_context_))
        , socket_(io_context_)
        , resolver_(io_context_)
        , is_connected_(false)
        , receive_buffer_(8192)  // 8KB receive buffer
    {}

    ~Impl() {
        Disconnect();
    }

    void StartIoThread() {
        io_thread_ = std::thread([this]() {
            io_thread_id_ = std::this_thread::get_id();
            try {
                io_context_.run();
            } catch (const std::exception& e) {
                std::cerr << "IO thread error: " << e.what() << std::endl;
            }
        });
    }

    void StopIoThread() {
        // Release work guard to allow io_context to exit
        work_guard_.reset();
        io_context_.stop();

        // Prevent self-join: don't join if called from IO thread
        if (io_thread_.joinable() && std::this_thread::get_id() != io_thread_id_) {
            io_thread_.join();
        }
    }

    void Disconnect() {
        std::lock_guard<std::mutex> lock(mutex_);
        if (socket_.is_open()) {
            asio::error_code ec;
            socket_.shutdown(asio::ip::tcp::socket::shutdown_both, ec);
            socket_.close(ec);
        }
        is_connected_ = false;
    }

    void StartReceive() {
        socket_.async_read_some(
            asio::buffer(receive_buffer_),
            [this](const asio::error_code& error, size_t bytes_transferred) {
                if (!error) {
                    if (receive_callback_) {
                        receive_callback_(receive_buffer_.data(), bytes_transferred);
                    }
                    StartReceive();  // Continue receiving
                } else {
                    HandleDisconnect();
                }
            }
        );
    }

    void HandleDisconnect() {
        std::function<void()> callback;
        {
            std::lock_guard<std::mutex> lock(mutex_);
            if (!is_connected_) {
                return;  // Already disconnected, avoid double callback
            }
            is_connected_ = false;
            callback = std::move(disconnect_callback_);  // Move to avoid use-after-free
            disconnect_callback_ = nullptr;  // Clear to prevent double-invocation
        }

        // Safe to call outside lock - callback is now local
        if (callback) {
            try {
                callback();
            } catch (const std::exception& e) {
                std::cerr << "Disconnect callback error: " << e.what() << std::endl;
            }
        }
    }
};

TcpConnection::TcpConnection()
    : impl_(std::make_unique<Impl>())
{}

TcpConnection::~TcpConnection() = default;

std::future<bool> TcpConnection::ConnectAsync(const std::string& host, uint16_t port) {
    auto promise = std::make_shared<std::promise<bool>>();
    auto future = promise->get_future();

    // Restart io_context if it was previously stopped (for reconnect)
    impl_->io_context_.restart();

    // Start IO thread
    impl_->StartIoThread();

    // Resolve and connect asynchronously
    impl_->resolver_.async_resolve(
        host,
        std::to_string(port),
        [this, promise](const asio::error_code& resolve_error,
                        const asio::ip::tcp::resolver::results_type& endpoints) {
            if (resolve_error) {
                promise->set_value(false);
                return;
            }

            asio::async_connect(
                impl_->socket_,
                endpoints,
                [this, promise](const asio::error_code& connect_error,
                                const asio::ip::tcp::endpoint&) {
                    if (!connect_error) {
                        std::lock_guard<std::mutex> lock(impl_->mutex_);
                        impl_->is_connected_ = true;
                        impl_->StartReceive();
                        promise->set_value(true);
                    } else {
                        promise->set_value(false);
                    }
                }
            );
        }
    );

    return future;
}

void TcpConnection::Disconnect() {
    impl_->Disconnect();
    impl_->StopIoThread();
}

bool TcpConnection::IsConnected() const {
    std::lock_guard<std::mutex> lock(impl_->mutex_);
    return impl_->is_connected_;
}

void TcpConnection::Send(const uint8_t* data, size_t size) {
    // Copy data to shared buffer to ensure it lives until async_write completes
    auto buffer = std::make_shared<std::vector<uint8_t>>(data, data + size);

    // Hold lock while checking connection status and initiating async_write
    // to prevent race condition where connection closes between check and write
    {
        std::lock_guard<std::mutex> lock(impl_->mutex_);
        if (!impl_->is_connected_) {
            throw std::runtime_error("Not connected");
        }

        // Async write with shared buffer ownership
        // Note: async_write itself is thread-safe and doesn't require lock held
        // but we need lock to ensure socket is valid when we initiate the operation
        asio::async_write(
            impl_->socket_,
            asio::buffer(*buffer),
            [buffer](const asio::error_code& error, size_t) {
                // buffer is captured by value to extend its lifetime
                if (error) {
                    std::cerr << "Send error: " << error.message() << std::endl;
                }
            }
        );
    }
}

void TcpConnection::SetReceiveCallback(std::function<void(const uint8_t*, size_t)> callback) {
    impl_->receive_callback_ = std::move(callback);
}

void TcpConnection::SetDisconnectCallback(std::function<void()> callback) {
    impl_->disconnect_callback_ = std::move(callback);
}

} // namespace internal
} // namespace playhouse
