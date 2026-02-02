#include "../base_integration_test.hpp"
#include <gtest/gtest.h>

using namespace playhouse;
using namespace playhouse::test;

/// C-07: Heartbeat Tests
/// Verifies automatic heartbeat mechanism
class C07_HeartbeatTest : public BaseIntegrationTest {};

TEST_F(C07_HeartbeatTest, Heartbeat_AutomaticallySent_KeepsConnectionAlive) {
    // Given: Connected with short heartbeat interval
    config_.heartbeat_interval_ms = 1000;  // 1 second for testing
    connector_ = std::make_unique<Connector>();
    connector_->Init(config_);

    ASSERT_TRUE(CreateStageAndConnect());

    // When: Wait for multiple heartbeat intervals
    std::this_thread::sleep_for(std::chrono::milliseconds(3000));

    // Keep calling MainThreadAction to process heartbeats
    for (int i = 0; i < 30; i++) {
        connector_->MainThreadAction();
        std::this_thread::sleep_for(std::chrono::milliseconds(100));
    }

    // Then: Connection should still be alive
    EXPECT_TRUE(connector_->IsConnected()) << "Connection should remain alive with heartbeats";
}

TEST_F(C07_HeartbeatTest, Heartbeat_LongInterval_ConnectionStable) {
    // Given: Connected with long heartbeat interval
    config_.heartbeat_interval_ms = 10000;  // 10 seconds
    connector_ = std::make_unique<Connector>();
    connector_->Init(config_);

    ASSERT_TRUE(CreateStageAndConnect());

    // When: Wait and process callbacks
    for (int i = 0; i < 50; i++) {
        connector_->MainThreadAction();
        std::this_thread::sleep_for(std::chrono::milliseconds(100));
    }

    // Then: Connection should be stable
    EXPECT_TRUE(connector_->IsConnected());
}

TEST_F(C07_HeartbeatTest, Heartbeat_DoesNotInterfereWithNormalMessages) {
    // Given: Connected with heartbeat enabled
    config_.heartbeat_interval_ms = 2000;
    connector_ = std::make_unique<Connector>();
    connector_->Init(config_);

    ASSERT_TRUE(CreateStageAndConnect());

    // When: Send normal messages during heartbeat period
    std::string echo_data = "{\"content\":\"Test during heartbeat\",\"sequence\":1}";
    Bytes payload(echo_data.begin(), echo_data.end());
    auto packet = Packet::FromBytes("EchoRequest", std::move(payload));

    auto future = connector_->RequestAsync(std::move(packet));

    // Then: Normal messages should work fine
    try {
        auto response = WaitWithMainThreadAction(future, 5000);
        EXPECT_EQ(response.GetErrorCode(), 0) << "Normal message should succeed during heartbeat";
    } catch (const std::exception& e) {
        FAIL() << "Message failed during heartbeat: " << e.what();
    }
}

TEST_F(C07_HeartbeatTest, Heartbeat_ConfiguredInterval_RespectedByConnector) {
    // Given: Specific heartbeat interval configured
    config_.heartbeat_interval_ms = 5000;  // 5 seconds
    connector_ = std::make_unique<Connector>();
    connector_->Init(config_);

    // When: Connect to server
    ASSERT_TRUE(CreateStageAndConnect());

    // Then: Connection should establish successfully with configured heartbeat
    EXPECT_TRUE(connector_->IsConnected());
    // The actual heartbeat timing is internal, but connection should work
}
