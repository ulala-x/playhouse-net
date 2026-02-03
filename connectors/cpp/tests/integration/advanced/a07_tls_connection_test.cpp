#include "../base_integration_test.hpp"
#include <gtest/gtest.h>
#include <chrono>
#include <thread>

using namespace playhouse;
using namespace playhouse::test;

/// A-07: TLS/WSS Connection Tests
/// Verifies TCP+TLS and WSS transport functionality
class A07_TlsConnectionTest : public ::testing::Test {
protected:
    void SetUp() override {
        connector_ = std::make_unique<Connector>();
        stage_info_ = BaseIntegrationTest::GetTestServer().GetOrCreateTestStage();
    }

    void TearDown() override {
        if (connector_) {
            if (connector_->IsConnected()) {
                connector_->Disconnect();
                std::this_thread::sleep_for(std::chrono::milliseconds(100));
            }
            connector_.reset();
        }
    }

    std::unique_ptr<Connector> connector_;
    CreateStageResponse stage_info_;

    bool WaitForCondition(std::function<bool()> condition, int timeout_ms = 5000) {
        auto deadline = std::chrono::steady_clock::now() + std::chrono::milliseconds(timeout_ms);
        while (!condition()) {
            if (std::chrono::steady_clock::now() >= deadline) {
                return false;
            }
            if (connector_) {
                connector_->MainThreadAction();
            }
            std::this_thread::sleep_for(std::chrono::milliseconds(10));
        }
        return true;
    }
};

TEST_F(A07_TlsConnectionTest, TcpTls_Connect_Authenticate_Echo_Succeeds) {
    ConnectorConfig config;
    config.request_timeout_ms = 5000;
    config.use_ssl = true;
    config.skip_server_certificate_validation = true;

    connector_->Init(config);

    auto future = connector_->ConnectAsync(
        BaseIntegrationTest::GetTestServer().GetHost(),
        BaseIntegrationTest::GetTestServer().GetTcpTlsPort()
    );
    (void)future;

    bool connected = false;
    bool error = false;
    connector_->OnConnect = [&connected]() { connected = true; };
    connector_->OnError = [&error](int, std::string) { error = true; };

    bool ok = WaitForCondition([&]() {
        return connected || error;
    }, 5000);

    ASSERT_TRUE(ok);
    ASSERT_TRUE(connector_->IsConnected());

    Bytes auth_payload = proto::EncodeAuthenticateRequest("tls-user-1", "valid_token");
    Packet auth_packet = Packet::FromBytes("AuthenticateRequest", std::move(auth_payload));
    bool auth_success = false;
    connector_->Authenticate(std::move(auth_packet), [&](bool success) {
        auth_success = success;
    });

    bool auth_ok = WaitForCondition([&]() {
        return auth_success;
    }, 5000);

    ASSERT_TRUE(auth_ok);

    Bytes echo_payload = proto::EncodeEchoRequest("Hello TLS", 1);
    Packet echo_packet = Packet::FromBytes("EchoRequest", std::move(echo_payload));
    Packet echo_response;

    bool echo_done = false;
    connector_->Request(std::move(echo_packet), [&](Packet response) {
        echo_response = std::move(response);
        echo_done = true;
    });

    bool echo_ok = WaitForCondition([&]() {
        return echo_done;
    }, 5000);

    ASSERT_TRUE(echo_ok);
    std::string echo_content;
    int32_t echo_sequence = 0;
    ASSERT_TRUE(proto::DecodeEchoReply(echo_response.GetPayload(), echo_content, echo_sequence));
    EXPECT_EQ(echo_content, "Hello TLS");
}

TEST_F(A07_TlsConnectionTest, Wss_Connect_Authenticate_Echo_Succeeds) {
    ConnectorConfig config;
    config.request_timeout_ms = 5000;
    config.use_websocket = true;
    config.use_ssl = true;
    config.skip_server_certificate_validation = true;
    config.websocket_path = "/ws";

    connector_->Init(config);

    auto future = connector_->ConnectAsync(
        BaseIntegrationTest::GetTestServer().GetHost(),
        BaseIntegrationTest::GetTestServer().GetHttpsPort()
    );
    (void)future;

    bool connected = false;
    bool error = false;
    connector_->OnConnect = [&connected]() { connected = true; };
    connector_->OnError = [&error](int, std::string) { error = true; };

    bool ok = WaitForCondition([&]() {
        return connected || error;
    }, 5000);

    ASSERT_TRUE(ok);
    ASSERT_TRUE(connector_->IsConnected());

    Bytes auth_payload = proto::EncodeAuthenticateRequest("wss-user-1", "valid_token");
    Packet auth_packet = Packet::FromBytes("AuthenticateRequest", std::move(auth_payload));
    bool auth_success = false;
    connector_->Authenticate(std::move(auth_packet), [&](bool success) {
        auth_success = success;
    });

    bool auth_ok = WaitForCondition([&]() {
        return auth_success;
    }, 5000);

    ASSERT_TRUE(auth_ok);

    Bytes echo_payload = proto::EncodeEchoRequest("Hello WSS", 2);
    Packet echo_packet = Packet::FromBytes("EchoRequest", std::move(echo_payload));
    Packet echo_response;

    bool echo_done = false;
    connector_->Request(std::move(echo_packet), [&](Packet response) {
        echo_response = std::move(response);
        echo_done = true;
    });

    bool echo_ok = WaitForCondition([&]() {
        return echo_done;
    }, 5000);

    ASSERT_TRUE(echo_ok);
    std::string echo_content;
    int32_t echo_sequence = 0;
    ASSERT_TRUE(proto::DecodeEchoReply(echo_response.GetPayload(), echo_content, echo_sequence));
    EXPECT_EQ(echo_content, "Hello WSS");
}
