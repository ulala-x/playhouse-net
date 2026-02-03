#include "ws_connection.hpp"
#include <deque>
#include <iostream>
#include <vector>

#include <boost/asio.hpp>
#include <boost/beast/core.hpp>
#include <boost/beast/websocket.hpp>

namespace playhouse {
namespace internal {

namespace asio = boost::asio;
namespace beast = boost::beast;
namespace websocket = beast::websocket;
using tcp = asio::ip::tcp;

class WsConnection::Impl {
public:
    asio::io_context io_context_;
    asio::executor_work_guard<asio::io_context::executor_type> work_guard_;
    asio::strand<asio::io_context::executor_type> strand_;
    tcp::resolver resolver_;
    websocket::stream<tcp::socket> ws_;
    std::string websocket_path_;
    bool is_connected_;
    std::function<void(const uint8_t*, size_t)> receive_callback_;
    std::function<void()> disconnect_callback_;
    std::thread io_thread_;
    std::mutex mutex_;
    std::thread::id io_thread_id_;
    beast::flat_buffer read_buffer_;
    std::deque<std::shared_ptr<std::vector<uint8_t>>> write_queue_;
    bool write_in_progress_;

    explicit Impl(std::string websocket_path)
        : io_context_()
        , work_guard_(asio::make_work_guard(io_context_))
        , strand_(asio::make_strand(io_context_))
        , resolver_(io_context_)
        , ws_(io_context_)
        , websocket_path_(std::move(websocket_path))
        , is_connected_(false)
        , write_in_progress_(false)
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
                std::cerr << "WebSocket IO thread error: " << e.what() << std::endl;
            }
        });
    }

    void StopIoThread() {
        work_guard_.reset();
        io_context_.stop();
        if (io_thread_.joinable() && std::this_thread::get_id() != io_thread_id_) {
            io_thread_.join();
        }
    }

    void Disconnect() {
        std::function<void()> callback;
        {
            std::lock_guard<std::mutex> lock(mutex_);
            if (ws_.is_open()) {
                beast::error_code ec;
                ws_.close(websocket::close_code::normal, ec);
            }
            if (!is_connected_) {
                return;
            }
            is_connected_ = false;
            callback = std::move(disconnect_callback_);
            disconnect_callback_ = nullptr;
        }
        asio::post(strand_, [this]() {
            write_queue_.clear();
            write_in_progress_ = false;
        });
        if (callback) {
            try {
                callback();
            } catch (const std::exception& e) {
                std::cerr << "WebSocket disconnect callback error: " << e.what() << std::endl;
            }
        }
    }

    void StartReceive() {
        ws_.async_read(
            read_buffer_,
            [this](const beast::error_code& error, size_t bytes_transferred) {
                if (!error) {
                    if (receive_callback_) {
                        std::vector<uint8_t> payload(bytes_transferred);
                        auto data = read_buffer_.data();
                        boost::asio::buffer_copy(
                            boost::asio::buffer(payload),
                            data,
                            bytes_transferred
                        );
                        receive_callback_(payload.data(), payload.size());
                    }
                    read_buffer_.consume(read_buffer_.size());
                    StartReceive();
                } else {
                    HandleDisconnect();
                }
            }
        );
    }

    void EnqueueWrite(const std::shared_ptr<std::vector<uint8_t>>& buffer) {
        write_queue_.push_back(buffer);
        if (write_in_progress_) {
            return;
        }
        write_in_progress_ = true;
        DoWrite();
    }

    void DoWrite() {
        if (write_queue_.empty()) {
            write_in_progress_ = false;
            return;
        }

        auto buffer = write_queue_.front();
        ws_.async_write(
            asio::buffer(*buffer),
            [this, buffer](const beast::error_code& error, size_t) {
                if (error) {
                    std::cerr << "WebSocket send error: " << error.message() << std::endl;
                    HandleDisconnect();
                    return;
                }
                write_queue_.pop_front();
                DoWrite();
            }
        );
    }

    void HandleDisconnect() {
        std::function<void()> callback;
        {
            std::lock_guard<std::mutex> lock(mutex_);
            if (!is_connected_) {
                return;
            }
            is_connected_ = false;
            callback = std::move(disconnect_callback_);
            disconnect_callback_ = nullptr;
        }
        asio::post(strand_, [this]() {
            write_queue_.clear();
            write_in_progress_ = false;
        });
        if (callback) {
            try {
                callback();
            } catch (const std::exception& e) {
                std::cerr << "WebSocket disconnect callback error: " << e.what() << std::endl;
            }
        }
    }
};

WsConnection::WsConnection(std::string websocket_path)
    : impl_(std::make_unique<Impl>(std::move(websocket_path)))
{}

WsConnection::~WsConnection() = default;

std::future<bool> WsConnection::ConnectAsync(const std::string& host, uint16_t port) {
    auto promise = std::make_shared<std::promise<bool>>();
    auto future = promise->get_future();

    impl_->io_context_.restart();
    impl_->StartIoThread();

    auto host_header = host + ":" + std::to_string(port);

    impl_->resolver_.async_resolve(
        host,
        std::to_string(port),
        [this, promise, host_header](const beast::error_code& resolve_error,
                                     const tcp::resolver::results_type& endpoints) {
            if (resolve_error) {
                promise->set_value(false);
                return;
            }

            asio::async_connect(
                impl_->ws_.next_layer(),
                endpoints,
                [this, promise, host_header](const beast::error_code& connect_error,
                                             const tcp::endpoint&) {
                    if (connect_error) {
                        promise->set_value(false);
                        return;
                    }

                    impl_->ws_.binary(true);
                    impl_->ws_.async_handshake(
                        host_header,
                        impl_->websocket_path_,
                        [this, promise](const beast::error_code& handshake_error) {
                            if (!handshake_error) {
                                {
                                    std::lock_guard<std::mutex> lock(impl_->mutex_);
                                    impl_->is_connected_ = true;
                                }
                                impl_->StartReceive();
                                promise->set_value(true);
                            } else {
                                promise->set_value(false);
                            }
                        }
                    );
                }
            );
        }
    );

    return future;
}

void WsConnection::Disconnect() {
    impl_->Disconnect();
    impl_->StopIoThread();
}

bool WsConnection::IsConnected() const {
    std::lock_guard<std::mutex> lock(impl_->mutex_);
    return impl_->is_connected_;
}

void WsConnection::Send(const uint8_t* data, size_t size) {
    auto buffer = std::make_shared<std::vector<uint8_t>>(data, data + size);

    {
        std::lock_guard<std::mutex> lock(impl_->mutex_);
        if (!impl_->is_connected_) {
            throw std::runtime_error("Not connected");
        }
    }

    asio::post(impl_->strand_, [impl = impl_.get(), buffer]() {
        impl->EnqueueWrite(buffer);
    });
}

void WsConnection::SetReceiveCallback(std::function<void(const uint8_t*, size_t)> callback) {
    impl_->receive_callback_ = std::move(callback);
}

void WsConnection::SetDisconnectCallback(std::function<void()> callback) {
    impl_->disconnect_callback_ = std::move(callback);
}

} // namespace internal
} // namespace playhouse
