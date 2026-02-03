#include "../base_integration_test.hpp"
#include <gtest/gtest.h>
#include <atomic>

using namespace playhouse;
using namespace playhouse::test;

/// C-08: Disconnection Tests
/// Verifies proper disconnection handling
class C08_DisconnectionTest : public BaseIntegrationTest {};

TEST_F(C08_DisconnectionTest, Disconnect_AfterConnection_IsConnectedReturnsFalse) {
    // Given: Connected to server
    ASSERT_TRUE(CreateStageAndConnect());
    EXPECT_TRUE(connector_->IsConnected());

    // When: Disconnect
    connector_->Disconnect();
    std::this_thread::sleep_for(std::chrono::milliseconds(500));

    // Then: IsConnected should return false
    EXPECT_FALSE(connector_->IsConnected()) << "IsConnected should be false after disconnect";
}

TEST_F(C08_DisconnectionTest, Disconnect_OnDisconnectEvent_Fires) {
    // Given: Connected to server
    ASSERT_TRUE(CreateStageAndConnect());

    std::atomic<bool> disconnect_event_fired{false};

    connector_->OnDisconnect = [&]() {
        disconnect_event_fired = true;
    };

    // When: Disconnect
    connector_->Disconnect();

    // Wait for event with MainThreadAction
    bool completed = WaitForConditionWithMainThreadAction([&]() {
        return disconnect_event_fired.load();
    }, 5000);

    // Then: OnDisconnect event should fire
    EXPECT_TRUE(completed) << "OnDisconnect event should fire within timeout";
    EXPECT_TRUE(disconnect_event_fired) << "OnDisconnect event should be triggered";
}

TEST_F(C08_DisconnectionTest, Disconnect_MultipleTimesSafely_NoErrors) {
    // Given: Connected to server
    ASSERT_TRUE(CreateStageAndConnect());

    // When: Disconnect multiple times
    connector_->Disconnect();
    std::this_thread::sleep_for(std::chrono::milliseconds(200));

    connector_->Disconnect();  // Second disconnect
    std::this_thread::sleep_for(std::chrono::milliseconds(200));

    connector_->Disconnect();  // Third disconnect

    // Then: Should handle gracefully without errors
    EXPECT_FALSE(connector_->IsConnected());
    SUCCEED() << "Multiple disconnects handled safely";
}

TEST_F(C08_DisconnectionTest, Disconnect_BeforeConnection_HandledGracefully) {
    // Given: Not yet connected
    EXPECT_FALSE(connector_->IsConnected());

    // When: Try to disconnect
    connector_->Disconnect();

    // Then: Should handle gracefully
    EXPECT_FALSE(connector_->IsConnected());
    SUCCEED() << "Disconnect before connection handled safely";
}

TEST_F(C08_DisconnectionTest, Send_AfterDisconnection_TriggersError) {
    // Given: Connected then disconnected
    ASSERT_TRUE(CreateStageAndConnect());
    connector_->Disconnect();
    std::this_thread::sleep_for(std::chrono::milliseconds(500));

    std::atomic<bool> error_triggered{false};
    int error_code = 0;

    connector_->OnError = [&](int code, std::string message) {
        error_triggered = true;
        error_code = code;
    };

    // When: Try to send after disconnection
    std::string data = "{\"test\":\"data\"}";
    Bytes payload(data.begin(), data.end());
    connector_->Send(Packet::FromBytes("TestMessage", std::move(payload)));

    // Process callbacks
    for (int i = 0; i < 10; i++) {
        connector_->MainThreadAction();
        std::this_thread::sleep_for(std::chrono::milliseconds(10));
    }

    // Then: Error should be triggered
    EXPECT_TRUE(error_triggered) << "Error event should be triggered for send after disconnect";
    EXPECT_EQ(error_code, error_code::CONNECTION_CLOSED) << "Error code should indicate connection closed";
}

TEST_F(C08_DisconnectionTest, Reconnect_AfterDisconnection_Succeeds) {
    // Given: Connected then disconnected
    ASSERT_TRUE(CreateStageAndConnect());
    connector_->Disconnect();
    std::this_thread::sleep_for(std::chrono::milliseconds(500));

    // When: Reconnect
    auto new_stage = GetTestServer().GetOrCreateTestStage();
    (void)new_stage;
    bool reconnected = ConnectAndWait(5000);

    // Then: Reconnection should succeed
    EXPECT_TRUE(reconnected) << "Reconnection should succeed";
    EXPECT_TRUE(connector_->IsConnected()) << "Should be connected after reconnection";
}
