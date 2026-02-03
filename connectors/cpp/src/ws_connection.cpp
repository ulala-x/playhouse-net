#include "ws_connection.hpp"
#include <deque>
#include <iostream>
#include <vector>

#include <boost/asio.hpp>
#include <boost/asio/ssl.hpp>
#include <boost/beast/core.hpp>
#include <boost/beast/websocket.hpp>

namespace playhouse {
namespace internal {

namespace asio = boost::asio;
namespace ssl = boost::asio::ssl;
namespace beast = boost::beast;
namespace websocket = beast::websocket;
using tcp = asio::ip::tcp;

class WsConnection::Impl {
public:
    asio::io_context io_context_;
    asio::executor_work_guard<asio::io_context::executor_type> work_guard_;
    asio::strand<asio::io_context::executor_type> strand_;
    tcp::resolver resolver_;
    ssl::context ssl_context_;
    websocket::stream<tcp::socket> ws_;
    websocket::stream<ssl::stream<tcp::socket>> wss_;
    std::string websocket_path_;
    bool use_ssl_;
    bool skip_server_certificate_validation_;
    bool is_connected_;
    std::function<void(const uint8_t*, size_t)> receive_callback_;
    std::function<void()> disconnect_callback_;
    std::thread io_thread_;
    std::mutex mutex_;
    std::thread::id io_thread_id_;
    beast::flat_buffer read_buffer_;
    std::deque<std::shared_ptr<std::vector<uint8_t>>> write_queue_;
    bool write_in_progress_;

    Impl(std::string websocket_path, bool use_ssl, bool skip_server_certificate_validation)
        : io_context_()
        , work_guard_(asio::make_work_guard(io_context_))
        , strand_(asio::make_strand(io_context_))
        , resolver_(io_context_)
        , ssl_context_(ssl::context::tls_client)
        , ws_(io_context_)
        , wss_(io_context_, ssl_context_)
        , websocket_path_(std::move(websocket_path))
        , use_ssl_(use_ssl)
        , skip_server_certificate_validation_(skip_server_certificate_validation)
        , is_connected_(false)
        , write_in_progress_(false)
    {
        if (use_ssl_) {
            if (skip_server_certificate_validation_) {
                ssl_context_.set_verify_mode(ssl::verify_none);
            } else {
                ssl_context_.set_default_verify_paths();
                ssl_context_.set_verify_mode(ssl::verify_peer);
            }
        }
    }

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
            if (use_ssl_) {
                if (wss_.is_open()) {
                    beast::error_code ec;
                    wss_.close(websocket::close_code::normal, ec);
                }
            } else if (ws_.is_open()) {
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
        auto read_handler = [this](const beast::error_code& error, size_t bytes_transferred) {
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
        };

        if (use_ssl_) {
            wss_.async_read(read_buffer_, std::move(read_handler));
        } else {
            ws_.async_read(read_buffer_, std::move(read_handler));
        }
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
        auto write_handler = [this, buffer](const beast::error_code& error, size_t) {
            if (error) {
                std::cerr << "WebSocket send error: " << error.message() << std::endl;
                HandleDisconnect();
                return;
            }
            write_queue_.pop_front();
            DoWrite();
        };

        if (use_ssl_) {
            wss_.async_write(asio::buffer(*buffer), std::move(write_handler));
        } else {
            ws_.async_write(asio::buffer(*buffer), std::move(write_handler));
        }
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

WsConnection::WsConnection(std::string websocket_path, bool use_ssl, bool skip_server_certificate_validation)
    : impl_(std::make_unique<Impl>(std::move(websocket_path), use_ssl, skip_server_certificate_validation))
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

            if (impl_->use_ssl_) {
                asio::async_connect(
                    beast::get_lowest_layer(impl_->wss_),
                    endpoints,
                    [this, promise, host_header](const beast::error_code& connect_error,
                                                 const tcp::endpoint&) {
                        if (connect_error) {
                            promise->set_value(false);
                            return;
                        }

                        impl_->wss_.next_layer().async_handshake(
                            ssl::stream_base::client,
                            [this, promise, host_header](const beast::error_code& tls_error) {
                                if (tls_error) {
                                    promise->set_value(false);
                                    return;
                                }

                                impl_->wss_.binary(true);
                                impl_->wss_.async_handshake(
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
            } else {
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
