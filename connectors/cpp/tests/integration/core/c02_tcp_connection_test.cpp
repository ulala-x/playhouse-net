#include "../base_integration_test.hpp"
#include <gtest/gtest.h>

using namespace playhouse;
using namespace playhouse::test;

/// C-02: TCP Connection Tests
/// Verifies that the Connector can successfully connect to the server's TCP port
class C02_TcpConnectionTest : public BaseIntegrationTest {};

TEST_F(C02_TcpConnectionTest, Connect_AfterStageCreation_Succeeds) {
    // Given: Stage has been created
    stage_info_ = GetTestServer().GetOrCreateTestStage();

    // When: Attempt TCP connection
    bool connected = ConnectAndWait(5000);

    // Then: Connection should succeed
    EXPECT_TRUE(connected) << "TCP connection should succeed";
    EXPECT_TRUE(connector_->IsConnected()) << "Connection state should be true";
}

TEST_F(C02_TcpConnectionTest, IsConnected_AfterConnection_ReturnsTrue) {
    // Given: Initially not connected
    EXPECT_FALSE(connector_->IsConnected()) << "Initial state should be not connected";

    // When: Connection succeeds
    ASSERT_TRUE(CreateStageAndConnect());

    // Then: IsConnected should return true
    EXPECT_TRUE(connector_->IsConnected()) << "IsConnected should be true after connection";
}

TEST_F(C02_TcpConnectionTest, OnConnect_Event_TriggersWithSuccess) {
    // Given: Stage created
    stage_info_ = GetTestServer().GetOrCreateTestStage();

    bool event_triggered = false;

    connector_->OnConnect = [&event_triggered]() {
        event_triggered = true;
    };

    // When: Connect asynchronously
    auto future = connector_->ConnectAsync(GetTestServer().GetHost(), GetTestServer().GetTcpPort());
    (void)future;

    // Wait for event with MainThreadAction
    bool condition_met = WaitForConditionWithMainThreadAction([&event_triggered]() {
        return event_triggered;
    }, 5000);

    // Then: Event should be triggered
    EXPECT_TRUE(condition_met) << "OnConnect event should fire within 5 seconds";
    EXPECT_TRUE(event_triggered) << "OnConnect event should be triggered";
}

TEST_F(C02_TcpConnectionTest, Connect_ToValidServer_TcpConnectionSucceeds) {
    // When: Connect to valid server
    bool connected = ConnectAndWait(5000);

    // Then: TCP connection itself should succeed
    // Note: Stage validation happens at a higher level
    EXPECT_TRUE(connected) << "TCP connection itself should succeed";
    EXPECT_TRUE(connector_->IsConnected()) << "Should be in connected state";
}

TEST_F(C02_TcpConnectionTest, Connect_MultipleTimes_Succeeds) {
    // Given: First connection
    ASSERT_TRUE(CreateStageAndConnect());
    EXPECT_TRUE(connector_->IsConnected());

    // When: Disconnect and reconnect
    connector_->Disconnect();
    std::this_thread::sleep_for(std::chrono::milliseconds(500));

    auto new_stage_info = GetTestServer().GetOrCreateTestStage();
    (void)new_stage_info;
    bool reconnected = ConnectAndWait(5000);

    // Then: Reconnection should succeed
    EXPECT_TRUE(reconnected) << "Reconnection should succeed";
    EXPECT_TRUE(connector_->IsConnected()) << "Should be connected after reconnection";
}
