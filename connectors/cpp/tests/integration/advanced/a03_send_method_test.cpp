#include "../base_integration_test.hpp"
#include <gtest/gtest.h>
#include <atomic>

using namespace playhouse;
using namespace playhouse::test;

/// A-03: Send Method Tests
/// Verifies fire-and-forget Send() method (no response expected)
class A03_SendMethodTest : public BaseIntegrationTest {};

TEST_F(A03_SendMethodTest, Send_SimpleMessage_NoResponse) {
    // Given: Connected to server
    ASSERT_TRUE(CreateStageAndConnect());

    // When: Send message without expecting response
    std::string notify_data = "{\"event\":\"UserAction\",\"action\":\"click\"}";
    Bytes payload(notify_data.begin(), notify_data.end());
    connector_->Send(Packet::FromBytes("NotifyRequest", std::move(payload)));

    // Process callbacks to ensure send completes
    for (int i = 0; i < 10; i++) {
        connector_->MainThreadAction();
        std::this_thread::sleep_for(std::chrono::milliseconds(10));
    }

    // Then: Should complete without error
    EXPECT_TRUE(connector_->IsConnected());
    SUCCEED() << "Send method completed without response";
}

TEST_F(A03_SendMethodTest, Send_MultipleConcurrent_AllSent) {
    // Given: Connected to server
    ASSERT_TRUE(CreateStageAndConnect());

    // When: Send multiple messages rapidly
    for (int i = 0; i < 10; i++) {
        std::string notify_data = "{\"event\":\"Event" + std::to_string(i) + "\"}";
        Bytes payload(notify_data.begin(), notify_data.end());
        connector_->Send(Packet::FromBytes("NotifyRequest", std::move(payload)));
    }

    // Process callbacks
    for (int i = 0; i < 20; i++) {
        connector_->MainThreadAction();
        std::this_thread::sleep_for(std::chrono::milliseconds(10));
    }

    // Then: All should be sent without blocking
    EXPECT_TRUE(connector_->IsConnected());
    SUCCEED() << "Multiple Send operations completed";
}

TEST_F(A03_SendMethodTest, Send_VsRequest_BothWork) {
    // Given: Connected to server
    ASSERT_TRUE(CreateStageAndConnect());

    // When: Mix Send and Request operations
    // Send without response
    std::string notify_data = "{\"event\":\"Notification\"}";
    Bytes notify_payload(notify_data.begin(), notify_data.end());
    connector_->Send(Packet::FromBytes("NotifyRequest", std::move(notify_payload)));

    // Request with response
    std::string echo_data = "{\"content\":\"Echo test\",\"sequence\":1}";
    Bytes echo_payload(echo_data.begin(), echo_data.end());
    auto packet = Packet::FromBytes("EchoRequest", std::move(echo_payload));

    // Then: Request should still work after Send
    Packet response = Packet::Empty("Empty");
    bool completed = RequestAndWait(std::move(packet), response, 5000);
    ASSERT_TRUE(completed) << "Request should complete after Send";
    EXPECT_EQ(response.GetErrorCode(), 0);
    SUCCEED() << "Send and Request both work together";
}

TEST_F(A03_SendMethodTest, Send_BeforeDisconnect_HandledGracefully) {
    // Given: Connected to server
    ASSERT_TRUE(CreateStageAndConnect());

    std::atomic<bool> error_triggered{false};

    connector_->OnError = [&](int code, std::string message) {
        if (code == error_code::CONNECTION_CLOSED) {
            error_triggered = true;
        }
    };

    // When: Disconnect then try to send
    connector_->Disconnect();
    std::this_thread::sleep_for(std::chrono::milliseconds(500));

    std::string notify_data = "{\"event\":\"AfterDisconnect\"}";
    Bytes payload(notify_data.begin(), notify_data.end());
    connector_->Send(Packet::FromBytes("NotifyRequest", std::move(payload)));

    // Process callbacks
    bool completed = WaitForConditionWithMainThreadAction([&]() {
        return error_triggered.load();
    }, 5000);

    // Then: Should trigger error event
    EXPECT_TRUE(completed) << "Error should be triggered for send after disconnect";
}

TEST_F(A03_SendMethodTest, Send_WithEmptyPayload_HandledCorrectly) {
    // Given: Connected to server
    ASSERT_TRUE(CreateStageAndConnect());

    // When: Send with empty payload
    Bytes empty_payload;
    connector_->Send(Packet::FromBytes("EmptyNotify", std::move(empty_payload)));

    // Process callbacks
    for (int i = 0; i < 10; i++) {
        connector_->MainThreadAction();
        std::this_thread::sleep_for(std::chrono::milliseconds(10));
    }

    // Then: Should handle gracefully
    EXPECT_TRUE(connector_->IsConnected());
    SUCCEED() << "Empty payload send handled gracefully";
}

TEST_F(A03_SendMethodTest, Send_LargePayload_Succeeds) {
    // Given: Connected to server
    ASSERT_TRUE(CreateStageAndConnect());

    // When: Send large payload via Send method
    std::string large_content(50 * 1024, 'X');  // 50KB
    std::string large_data = "{\"data\":\"" + large_content + "\"}";
    Bytes payload(large_data.begin(), large_data.end());
    connector_->Send(Packet::FromBytes("LargeNotify", std::move(payload)));

    // Process callbacks
    for (int i = 0; i < 50; i++) {
        connector_->MainThreadAction();
        std::this_thread::sleep_for(std::chrono::milliseconds(20));
    }

    // Then: Should handle without error
    EXPECT_TRUE(connector_->IsConnected());
    SUCCEED() << "Large payload sent successfully";
}
