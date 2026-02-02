#ifndef PLAYHOUSE_TEST_SERVER_FIXTURE_HPP
#define PLAYHOUSE_TEST_SERVER_FIXTURE_HPP

#include <string>
#include <cstdint>
#include <memory>
#include <optional>

namespace playhouse::test {

/// Stage creation response from test server
struct CreateStageResponse {
    bool success;
    int64_t stage_id;
    std::string stage_type;
    std::string reply_payload_id;
};

/// Test server connection manager fixture
/// Manages HTTP API calls to the test server for creating stages and TCP connection information
/// Environment variables for server configuration:
/// - TEST_SERVER_HOST (default: localhost)
/// - TEST_SERVER_HTTP_PORT (default: 8080)
/// - TEST_SERVER_TCP_PORT (default: 34001)
/// - TEST_SERVER_WS_PORT (default: 8080, WebSocket uses same port as HTTP)
class TestServerFixture {
public:
    /// Constructor
    TestServerFixture();

    /// Destructor
    ~TestServerFixture();

    /// Get test server host
    const std::string& GetHost() const { return host_; }

    /// Get test server TCP port
    uint16_t GetTcpPort() const { return tcp_port_; }

    /// Get test server HTTP port
    uint16_t GetHttpPort() const { return http_port_; }

    /// Get test server WebSocket port (typically same as HTTP port)
    uint16_t GetWsPort() const { return ws_port_; }

    /// Create a test stage via HTTP API
    /// @param stage_type Stage type to create
    /// @param max_players Maximum players (optional)
    /// @return Stage creation response
    CreateStageResponse CreateStage(const std::string& stage_type, std::optional<int> max_players = std::nullopt);

    /// Create a test stage with default "TestStage" type
    /// @return Stage creation response
    CreateStageResponse CreateTestStage();

private:
    class Impl;
    std::unique_ptr<Impl> impl_;

    std::string host_;
    uint16_t tcp_port_;
    uint16_t http_port_;
    uint16_t ws_port_;
};

} // namespace playhouse::test

#endif // PLAYHOUSE_TEST_SERVER_FIXTURE_HPP
