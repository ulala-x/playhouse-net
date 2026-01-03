#include "echo_client.h"
#include <iostream>
#include <random>
#include <algorithm>
#include <functional>

using namespace std::literals;

namespace playhouse {

// ========================================================================
// EchoSocket Implementation
// ========================================================================

void EchoSocket::on_connect()
{
    // Call base class implementation first
    PlayHouseSocket::on_connect();

    // Notify client
    if (m_client) {
        m_client->on_socket_connect(this);
    }
}

void EchoSocket::on_disconnect() noexcept
{
    // Notify client
    if (m_client) {
        m_client->on_socket_disconnect(this);
    }

    // Call base class implementation
    PlayHouseSocket::on_disconnect();
}

int EchoSocket::on_message(const const_buffer& msg)
{
    // IMPORTANT: We need to parse the message BEFORE calling base class
    // because base class already handles the packet.
    // Instead, we'll duplicate some logic here.

    const uint8_t* data = static_cast<const uint8_t*>(msg.data());
    size_t size = msg.size();

    if (size < 4) {
        return 0;
    }

    uint32_t body_length = Codec::read_int32_le(data);

    if (size < 4 + body_length) {
        return 0;
    }

    const uint8_t* body_data = data + 4;

    // Decode the response packet
    std::string msg_id;
    uint16_t msg_seq;
    int64_t stage_id;
    uint16_t error_code;
    std::vector<uint8_t> payload;

    if (Codec::decode_response(body_data, body_length, msg_id, msg_seq, stage_id, error_code, payload)) {
        // Handle authentication
        if (msg_id == "AuthenticateReply") {
            m_authenticated = true;
        }

        // Notify client
        if (m_client) {
            m_client->on_socket_message(this, msg_id, msg_seq, stage_id, error_code, payload);
        }

        // Relay echo mode: auto-respond to EchoReply
        if (s_enable_relay_echo && msg_id == "EchoReply") {
            if (!m_last_echo_content.empty()) {
                send_echo_request(m_last_echo_content, get_current_timestamp_ms());
            }
        }
    }

    return static_cast<int>(4 + body_length);
}

// ========================================================================
// Constructor / Destructor
// ========================================================================

EchoClient::EchoClient()
    : m_host("127.0.0.1")
    , m_port(16110)
    , m_base_stage_id(10000)
    , m_connect_min(500)
    , m_connect_max(800)
    , m_message_size_index(0)
    , m_times(200)
    , m_last_send_count(0)
    , m_last_recv_count(0)
    , m_last_send_bytes(0)
    , m_last_recv_bytes(0)
    , m_last_stats_time(std::chrono::steady_clock::now())
{
}

EchoClient::~EchoClient()
{
    stop();
}

// ========================================================================
// Server Configuration
// ========================================================================

void EchoClient::set_endpoint(const std::string& host, int port)
{
    m_host = host;
    m_port = port;
}

// ========================================================================
// Connection Test Mode
// ========================================================================

void EchoClient::toggle_connect_test()
{
    m_enable_connect_test = !m_enable_connect_test;
}

void EchoClient::set_connect_range(int64_t min, int64_t max)
{
    m_connect_min = min;
    m_connect_max = max;
}

void EchoClient::add_connect_min(int64_t delta)
{
    m_connect_min += delta;
    if (m_connect_min > m_connect_max) {
        m_connect_max = m_connect_min;
    }
}

void EchoClient::sub_connect_min(int64_t delta)
{
    if (m_connect_min < delta) {
        m_connect_min = 0;
    } else {
        m_connect_min -= delta;
    }
}

void EchoClient::add_connect_max(int64_t delta)
{
    m_connect_max += delta;
}

void EchoClient::sub_connect_max(int64_t delta)
{
    if (m_connect_max < 100 + delta) {
        m_connect_max = 100;
    } else {
        m_connect_max -= delta;
    }

    if (m_connect_max < m_connect_min) {
        m_connect_min = m_connect_max;
    }
}

// ========================================================================
// Traffic Test Mode
// ========================================================================

void EchoClient::toggle_traffic_test()
{
    m_enable_traffic_test = !m_enable_traffic_test;
}

void EchoClient::set_message_size_index(size_t index)
{
    if (index >= MESSAGE_TYPE_COUNT) {
        return;
    }
    m_message_size_index = index;
}

void EchoClient::increase_message_size()
{
    if (m_message_size_index >= MESSAGE_TYPE_COUNT - 1) {
        return;
    }
    ++m_message_size_index;
}

void EchoClient::decrease_message_size()
{
    if (m_message_size_index == 0) {
        return;
    }
    --m_message_size_index;
}

void EchoClient::set_times(int64_t times)
{
    if (times > 0) {
        m_times = times;
    } else {
        m_times = 1;
    }
}

void EchoClient::add_times(int64_t delta)
{
    m_times += delta;
}

void EchoClient::sub_times(int64_t delta)
{
    if ((m_times + 1) > delta) {
        m_times -= delta;
    } else {
        m_times = 1;
    }
}

// ========================================================================
// Relay Echo Mode
// ========================================================================

void EchoClient::toggle_relay_echo()
{
    PlayHouseSocket::s_enable_relay_echo = !PlayHouseSocket::s_enable_relay_echo;
}

// ========================================================================
// Connection Management
// ========================================================================

void EchoClient::request_connect(int64_t count)
{
    if (!m_connector) {
        return;
    }

    // Prepare endpoint
    boost::asio::ip::tcp::endpoint endpoint;
    try {
        auto address = boost::asio::ip::address::from_string(m_host);
        endpoint = boost::asio::ip::tcp::endpoint(address, static_cast<unsigned short>(m_port));
    } catch (...) {
        std::cerr << "Invalid endpoint: " << m_host << ":" << m_port << std::endl;
        return;
    }

    // Create connections
    for (int64_t i = 0; i < count; ++i) {
        m_connector->request_connect(endpoint);
    }
}

void EchoClient::request_disconnect(int64_t count)
{
    if (!m_connector) {
        return;
    }

    int64_t disconnected = 0;

    std::vector<std::shared_ptr<asio::Isocket_tcp>> sockets;
    {
        std::lock_guard lock(m_connector->get_lockable());
        sockets.reserve(m_connector->count());
        sockets.assign(m_connector->begin(), m_connector->end());
    }

    for (auto& socket : sockets) {
        if (socket->closesocket()) {
            if (++disconnected >= count) {
                break;
            }
        }
    }
}

void EchoClient::request_disconnect_all()
{
    if (!m_connector) {
        return;
    }

    m_connector->close_connectable_all();
}

// ========================================================================
// Message Sending
// ========================================================================

void EchoClient::request_send_immediately(int64_t count)
{
    send_to_all_sockets(MESSAGE_TYPES[m_message_size_index].size, count);
}

// ========================================================================
// Lifecycle
// ========================================================================

void EchoClient::start()
{
    // Create custom connector that sets up sockets
    class EchoConnector : public asio::connector<EchoSocket> {
    public:
        EchoConnector(EchoClient* client) : m_client(client) {}

        virtual std::shared_ptr<asio::Isocket_tcp> process_create_socket() override {
            auto socket = std::make_shared<EchoSocket>();
            socket->set_client(m_client);
            return socket;
        }

    private:
        EchoClient* m_client;
    };

    // Create connector
    m_connector = std::make_shared<EchoConnector>(this);
    m_connector->start();

    // Reset statistics
    m_send_count = 0;
    m_recv_count = 0;
    m_send_bytes = 0;
    m_recv_bytes = 0;
    m_last_send_count = 0;
    m_last_recv_count = 0;
    m_last_send_bytes = 0;
    m_last_recv_bytes = 0;
    m_last_stats_time = std::chrono::steady_clock::now();

    // Start processing thread
    m_running = true;
    m_process_thread = std::make_unique<std::thread>([this]() {
        process_loop();
    });
}

void EchoClient::stop()
{
    // Stop processing thread
    m_running = false;
    if (m_process_thread && m_process_thread->joinable()) {
        m_process_thread->join();
    }

    // Close all connections
    if (m_connector) {
        m_connector->close();
        m_connector.reset();
    }
}

// ========================================================================
// Statistics
// ========================================================================

int64_t EchoClient::get_connection_count() const
{
    if (!m_connector) {
        return 0;
    }

    std::lock_guard lock(m_connector->get_lockable());
    return m_connector->count();
}

int64_t EchoClient::get_send_count() const
{
    return m_send_count.load();
}

int64_t EchoClient::get_recv_count() const
{
    return m_recv_count.load();
}

double EchoClient::get_send_rate() const
{
    std::lock_guard lock(m_stats_mutex);

    auto now = std::chrono::steady_clock::now();
    auto elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(now - m_last_stats_time);

    if (elapsed.count() == 0) {
        return 0.0;
    }

    auto current_send = m_send_count.load();
    auto delta = current_send - m_last_send_count;

    return (static_cast<double>(delta) * 1000.0) / elapsed.count();
}

double EchoClient::get_recv_rate() const
{
    std::lock_guard lock(m_stats_mutex);

    auto now = std::chrono::steady_clock::now();
    auto elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(now - m_last_stats_time);

    if (elapsed.count() == 0) {
        return 0.0;
    }

    auto current_recv = m_recv_count.load();
    auto delta = current_recv - m_last_recv_count;

    return (static_cast<double>(delta) * 1000.0) / elapsed.count();
}

double EchoClient::get_send_bytes_rate() const
{
    std::lock_guard lock(m_stats_mutex);

    auto now = std::chrono::steady_clock::now();
    auto elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(now - m_last_stats_time);

    if (elapsed.count() == 0) {
        return 0.0;
    }

    auto current_bytes = m_send_bytes.load();
    auto delta = current_bytes - m_last_send_bytes;

    return (static_cast<double>(delta) * 1000.0) / elapsed.count();
}

double EchoClient::get_recv_bytes_rate() const
{
    std::lock_guard lock(m_stats_mutex);

    auto now = std::chrono::steady_clock::now();
    auto elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(now - m_last_stats_time);

    if (elapsed.count() == 0) {
        return 0.0;
    }

    auto current_bytes = m_recv_bytes.load();
    auto delta = current_bytes - m_last_recv_bytes;

    return (static_cast<double>(delta) * 1000.0) / elapsed.count();
}

// ========================================================================
// Internal Methods
// ========================================================================

void EchoClient::process_loop()
{
    while (m_running) {
        // Process traffic test
        process_traffic_test();

        // Process connection test
        process_connect_test();

        // Update statistics baseline
        {
            std::lock_guard lock(m_stats_mutex);
            m_last_send_count = m_send_count.load();
            m_last_recv_count = m_recv_count.load();
            m_last_send_bytes = m_send_bytes.load();
            m_last_recv_bytes = m_recv_bytes.load();
            m_last_stats_time = std::chrono::steady_clock::now();
        }

        // Sleep
        std::this_thread::sleep_for(100ms);
    }
}

void EchoClient::process_connect_test()
{
    if (!m_enable_connect_test) {
        return;
    }

    if (!m_connector) {
        return;
    }

    int64_t current_count = get_connection_count();
    int64_t disconnected = 0;

    // Disconnect logic
    if (current_count > m_connect_min) {
        std::vector<std::shared_ptr<asio::Isocket_tcp>> sockets;
        {
            std::lock_guard lock(m_connector->get_lockable());
            sockets.reserve(m_connector->count());
            sockets.assign(m_connector->begin(), m_connector->end());
        }

        int64_t range = current_count - m_connect_min;
        int64_t disconnect_count = (range > 0) ? rand() % range : 0;

        for (auto& socket : sockets) {
            if (socket->closesocket()) {
                if (++disconnected >= disconnect_count) {
                    break;
                }
            }
        }
    }

    // Connect logic
    current_count -= disconnected;

    int64_t connect_count = 0;
    if (current_count < m_connect_min) {
        connect_count = m_connect_min - current_count;
    } else if (current_count < m_connect_max) {
        int64_t range = m_connect_max - current_count;
        connect_count = (range > 0) ? rand() % range : 0;
    }

    if (connect_count > 0) {
        request_connect(connect_count);
    }
}

void EchoClient::process_traffic_test()
{
    if (!m_enable_traffic_test) {
        return;
    }

    send_to_all_sockets(MESSAGE_TYPES[m_message_size_index].size, m_times);
}

void EchoClient::generate_echo_content(size_t size, std::string& out)
{
    out.resize(size);

    static std::random_device rd;
    static std::mt19937 gen(rd());
    static std::uniform_int_distribution<> dis(0, 255);

    for (size_t i = 0; i < size; ++i) {
        out[i] = static_cast<char>(dis(gen));
    }
}

void EchoClient::send_to_all_sockets(size_t message_size, int64_t count)
{
    if (!m_connector || count <= 0) {
        return;
    }

    // Get all connected sockets
    std::vector<std::shared_ptr<asio::Isocket_tcp>> sockets;
    {
        std::lock_guard lock(m_connector->get_lockable());
        sockets.reserve(m_connector->count());
        sockets.assign(m_connector->begin(), m_connector->end());
    }

    if (sockets.empty()) {
        return;
    }

    // Generate echo content
    std::string echo_content;
    generate_echo_content(message_size, echo_content);

    // Send to all sockets
    for (auto& socket_base : sockets) {
        // Downcast to EchoSocket
        auto socket = std::dynamic_pointer_cast<EchoSocket>(socket_base);
        if (!socket) {
            continue;
        }

        // Skip if not authenticated
        if (!socket->is_authenticated()) {
            continue;
        }

        // Send multiple times
        for (int64_t i = 0; i < count; ++i) {
            auto timestamp = PlayHouseSocket::get_current_timestamp_ms();
            if (socket->send_echo_request(echo_content, timestamp)) {
                m_send_count.fetch_add(1);
                m_send_bytes.fetch_add(message_size);
            }
        }
    }
}

// ========================================================================
// Socket Event Handlers
// ========================================================================

void EchoClient::on_socket_connect(EchoSocket* socket)
{
    // Assign stage ID
    int64_t stage_id = m_base_stage_id + m_next_stage_id.fetch_add(1);
    socket->set_stage_id(stage_id);

    // Send authentication
    socket->send_authenticate("1.0.0");
}

void EchoClient::on_socket_disconnect(EchoSocket* socket)
{
    // Nothing to do
}

void EchoClient::on_socket_message(EchoSocket* socket,
                                  const std::string& msg_id,
                                  uint16_t msg_seq,
                                  int64_t stage_id,
                                  uint16_t error_code,
                                  const std::vector<uint8_t>& payload)
{
    // Track received messages
    if (msg_id == "EchoReply") {
        m_recv_count.fetch_add(1);
        m_recv_bytes.fetch_add(payload.size());
    }
}

} // namespace playhouse
