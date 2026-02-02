#include "../base_integration_test.hpp"
#include <gtest/gtest.h>
#include <atomic>

using namespace playhouse;
using namespace playhouse::test;

/// C-06: Push Message Tests
/// Verifies server-initiated push messages are received correctly
class C06_PushMessageTest : public BaseIntegrationTest {};

TEST_F(C06_PushMessageTest, PushMessage_OnReceiveEvent_TriggersCorrectly) {
    // Given: Connected and authenticated
    ASSERT_TRUE(CreateStageAndConnect());

    std::atomic<bool> message_received{false};
    std::string received_msg_id;

    connector_->OnReceive = [&](Packet packet) {
        message_received = true;
        received_msg_id = packet.GetMsgId();
    };

    // When: Trigger a push message by sending a broadcast request
    std::string broadcast_data = "{\"content\":\"Push Test\"}";
    Bytes payload(broadcast_data.begin(), broadcast_data.end());
    connector_->Send(Packet::FromBytes("BroadcastRequest", std::move(payload)));

    // Then: Push message should be received
    bool completed = WaitForConditionWithMainThreadAction([&]() {
        return message_received.load();
    }, 5000);

    EXPECT_TRUE(completed) << "Push message should be received within timeout";
    EXPECT_TRUE(message_received) << "OnReceive event should be triggered";
    EXPECT_FALSE(received_msg_id.empty()) << "Received message should have an ID";
}

TEST_F(C06_PushMessageTest, PushMessage_MsgSeqIsZero_IndicatesPush) {
    // Given: Connected and authenticated
    ASSERT_TRUE(CreateStageAndConnect());

    std::atomic<bool> message_received{false};
    uint16_t msg_seq = 999;

    connector_->OnReceive = [&](Packet packet) {
        msg_seq = packet.GetMsgSeq();
        message_received = true;
    };

    // When: Trigger a push message
    std::string broadcast_data = "{\"content\":\"Push Seq Test\"}";
    Bytes payload(broadcast_data.begin(), broadcast_data.end());
    connector_->Send(Packet::FromBytes("BroadcastRequest", std::move(payload)));

    // Then: Message sequence should be 0 (push message indicator)
    bool completed = WaitForConditionWithMainThreadAction([&]() {
        return message_received.load();
    }, 5000);

    EXPECT_TRUE(completed);
    EXPECT_EQ(msg_seq, 0) << "Push messages should have MsgSeq == 0";
}

TEST_F(C06_PushMessageTest, PushMessage_Multiple_AllReceived) {
    // Given: Connected and authenticated
    ASSERT_TRUE(CreateStageAndConnect());

    std::atomic<int> message_count{0};

    connector_->OnReceive = [&](Packet packet) {
        message_count++;
    };

    // When: Trigger multiple push messages
    for (int i = 0; i < 3; i++) {
        std::string broadcast_data = "{\"content\":\"Push " + std::to_string(i) + "\"}";
        Bytes payload(broadcast_data.begin(), broadcast_data.end());
        connector_->Send(Packet::FromBytes("BroadcastRequest", std::move(payload)));

        // Small delay between sends
        std::this_thread::sleep_for(std::chrono::milliseconds(100));
    }

    // Then: All push messages should be received
    bool completed = WaitForConditionWithMainThreadAction([&]() {
        return message_count.load() >= 3;
    }, 10000);

    EXPECT_TRUE(completed) << "All push messages should be received";
    EXPECT_GE(message_count.load(), 3) << "Should receive at least 3 push messages";
}

TEST_F(C06_PushMessageTest, PushMessage_WithPayload_PayloadAccessible) {
    // Given: Connected and authenticated
    ASSERT_TRUE(CreateStageAndConnect());

    std::atomic<bool> message_received{false};
    bool payload_not_empty = false;

    connector_->OnReceive = [&](Packet packet) {
        payload_not_empty = !packet.GetPayload().empty();
        message_received = true;
    };

    // When: Trigger a push message
    std::string broadcast_data = "{\"content\":\"Push with payload\"}";
    Bytes payload(broadcast_data.begin(), broadcast_data.end());
    connector_->Send(Packet::FromBytes("BroadcastRequest", std::move(payload)));

    // Then: Payload should be accessible
    bool completed = WaitForConditionWithMainThreadAction([&]() {
        return message_received.load();
    }, 5000);

    EXPECT_TRUE(completed);
    EXPECT_TRUE(payload_not_empty) << "Push message should have non-empty payload";
}
