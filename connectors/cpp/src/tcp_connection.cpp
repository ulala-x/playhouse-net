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
    asio::ip::tcp::socket socket_;
    asio::ip::tcp::resolver resolver_;
    bool is_connected_;
    std::function<void(const uint8_t*, size_t)> receive_callback_;
    std::function<void()> disconnect_callback_;
    std::vector<uint8_t> receive_buffer_;
    std::thread io_thread_;
    std::mutex mutex_;

    Impl()
        : io_context_()
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
            try {
                io_context_.run();
            } catch (const std::exception& e) {
                std::cerr << "IO thread error: " << e.what() << std::endl;
            }
        });
    }

    void StopIoThread() {
        io_context_.stop();
        if (io_thread_.joinable()) {
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
        std::lock_guard<std::mutex> lock(mutex_);
        is_connected_ = false;
        if (disconnect_callback_) {
            disconnect_callback_();
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
    if (!IsConnected()) {
        throw std::runtime_error("Not connected");
    }

    // Async write
    asio::async_write(
        impl_->socket_,
        asio::buffer(data, size),
        [](const asio::error_code& error, size_t) {
            if (error) {
                std::cerr << "Send error: " << error.message() << std::endl;
            }
        }
    );
}

void TcpConnection::SetReceiveCallback(std::function<void(const uint8_t*, size_t)> callback) {
    impl_->receive_callback_ = std::move(callback);
}

void TcpConnection::SetDisconnectCallback(std::function<void()> callback) {
    impl_->disconnect_callback_ = std::move(callback);
}

} // namespace internal
} // namespace playhouse
