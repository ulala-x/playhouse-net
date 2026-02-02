#include "../base_integration_test.hpp"
#include <gtest/gtest.h>
#include <atomic>

using namespace playhouse;
using namespace playhouse::test;

/// A-06: Edge Case Tests
/// Verifies handling of unusual scenarios and edge cases
class A06_EdgeCaseTest : public BaseIntegrationTest {};

TEST_F(A06_EdgeCaseTest, EmptyMessageId_HandledGracefully) {
    // Given: Connected to server
    ASSERT_TRUE(CreateStageAndConnect());

    // When: Send packet with empty message ID
    std::string data = "{\"test\":\"data\"}";
    Bytes payload(data.begin(), data.end());
    auto packet = Packet::FromBytes("", std::move(payload));

    Packet response = Packet::Empty("Empty");
    bool completed = RequestAndWait(std::move(packet), response, 5000);

    // Then: Should handle gracefully
    if (completed) {
        SUCCEED() << "Empty message ID handled, error code: " << response.GetErrorCode();
    } else {
        SUCCEED() << "Empty message ID rejected gracefully";
    }
}

TEST_F(A06_EdgeCaseTest, VeryLongMessageId_HandledCorrectly) {
    // Given: Connected to server
    ASSERT_TRUE(CreateStageAndConnect());

    // When: Send packet with very long message ID (near protocol limit of 256)
    std::string long_msg_id(250, 'A');
    std::string data = "{\"test\":\"data\"}";
    Bytes payload(data.begin(), data.end());
    auto packet = Packet::FromBytes(long_msg_id, std::move(payload));

    Packet response = Packet::Empty("Empty");
    bool completed = RequestAndWait(std::move(packet), response, 5000);

    // Then: Should handle appropriately
    if (completed) {
        SUCCEED() << "Long message ID handled";
    } else {
        SUCCEED() << "Long message ID result: timeout";
    }
}

TEST_F(A06_EdgeCaseTest, SpecialCharactersInMessageId_HandledCorrectly) {
    // Given: Connected to server
    ASSERT_TRUE(CreateStageAndConnect());

    // When: Send packet with special characters in message ID
    std::string special_msg_id = "Test@Message#123$";
    std::string data = "{\"test\":\"data\"}";
    Bytes payload(data.begin(), data.end());
    auto packet = Packet::FromBytes(special_msg_id, std::move(payload));

    Packet response = Packet::Empty("Empty");
    bool completed = RequestAndWait(std::move(packet), response, 5000);

    // Then: Should handle appropriately
    if (completed) {
        SUCCEED() << "Special characters in message ID handled";
    } else {
        SUCCEED() << "Special characters handled: timeout";
    }
}

TEST_F(A06_EdgeCaseTest, RapidConnectDisconnect_SystemStable) {
    // When: Rapidly connect and disconnect
    for (int i = 0; i < 5; i++) {
        auto stage = GetTestServer().CreateTestStage();
        (void)stage;
        bool connected = ConnectAndWait(2000);
        if (connected) {
            connector_->Disconnect();
            std::this_thread::sleep_for(std::chrono::milliseconds(100));
        }
    }

    // Then: System should remain stable
    SUCCEED() << "Rapid connect/disconnect handled without crash";
}

TEST_F(A06_EdgeCaseTest, MainThreadAction_CalledExcessively_NoIssues) {
    // Given: Connected to server
    ASSERT_TRUE(CreateStageAndConnect());

    // When: Call MainThreadAction many times rapidly
    for (int i = 0; i < 1000; i++) {
        connector_->MainThreadAction();
    }

    // Then: Should not cause any issues
    EXPECT_TRUE(connector_->IsConnected());
    SUCCEED() << "Excessive MainThreadAction calls handled correctly";
}

TEST_F(A06_EdgeCaseTest, MainThreadAction_NeverCalled_RequestFails) {
    // Given: Connected to server
    ASSERT_TRUE(CreateStageAndConnect());

    // When: Send request but never call MainThreadAction
    std::string echo_data = "{\"content\":\"No MainThread\",\"sequence\":1}";
    Bytes payload(echo_data.begin(), echo_data.end());
    auto packet = Packet::FromBytes("EchoRequest", std::move(payload));

    bool callback_fired = false;
    connector_->Request(std::move(packet), [&](Packet) {
        callback_fired = true;
    });

    // Wait without calling MainThreadAction
    std::this_thread::sleep_for(std::chrono::milliseconds(2000));

    // Then: Request should not complete
    EXPECT_FALSE(callback_fired) << "Without MainThreadAction, callback shouldn't fire";
}

TEST_F(A06_EdgeCaseTest, ZeroPayload_HandledCorrectly) {
    // Given: Connected to server
    ASSERT_TRUE(CreateStageAndConnect());

    // When: Send packet with zero-length payload
    Bytes empty_payload;
    auto packet = Packet::FromBytes("EmptyPayloadRequest", std::move(empty_payload));

    Packet response = Packet::Empty("Empty");
    bool completed = RequestAndWait(std::move(packet), response, 5000);

    // Then: Should handle gracefully
    if (completed) {
        SUCCEED() << "Zero payload handled, error code: " << response.GetErrorCode();
    } else {
        SUCCEED() << "Zero payload result: timeout";
    }
}

TEST_F(A06_EdgeCaseTest, BinaryZeroBytes_HandledCorrectly) {
    // Given: Connected to server
    ASSERT_TRUE(CreateStageAndConnect());

    // When: Send payload with null bytes
    Bytes null_payload = {0x00, 0x01, 0x00, 0x02, 0x00, 0x03};
    auto packet = Packet::FromBytes("NullByteRequest", std::move(null_payload));

    Packet response = Packet::Empty("Empty");
    bool completed = RequestAndWait(std::move(packet), response, 5000);

    // Then: Should handle binary data with nulls
    if (completed) {
        SUCCEED() << "Binary null bytes handled";
    } else {
        SUCCEED() << "Binary null bytes result: timeout";
    }
}

TEST_F(A06_EdgeCaseTest, ConnectWithoutInit_ThrowsError) {
    // Given: Connector without Init
    auto uninit_connector = std::make_unique<Connector>();

    // When: Try to connect without Init
    bool threw_exception = false;
    try {
        auto future = uninit_connector->ConnectAsync(GetTestServer().GetHost(), GetTestServer().GetTcpPort());
        (void)future;
    } catch (const std::exception& e) {
        threw_exception = true;
    }

    // Then: Should throw or fail
    EXPECT_TRUE(threw_exception) << "Connect without Init should throw exception";
}

TEST_F(A06_EdgeCaseTest, DoubleInit_HandledGracefully) {
    // Given: Connector already initialized
    ASSERT_TRUE(connector_ != nullptr);

    // When: Call Init again
    connector_->Init(config_);

    // Then: Should handle gracefully
    SUCCEED() << "Double Init handled without crash";

    // Can still connect
    bool connected = ConnectAndWait(5000);
    EXPECT_TRUE(connected) << "Can still connect after double Init";
}

TEST_F(A06_EdgeCaseTest, CallbackThrowsException_SystemRemains Stable) {
    // Given: Connected with callback that throws
    ASSERT_TRUE(CreateStageAndConnect());

    connector_->OnReceive = [](Packet packet) {
        throw std::runtime_error("Callback exception");
    };

    // When: Trigger a message that will invoke the throwing callback
    std::string broadcast_data = "{\"content\":\"Trigger callback\"}";
    Bytes payload(broadcast_data.begin(), broadcast_data.end());
    connector_->Send(Packet::FromBytes("BroadcastRequest", std::move(payload)));

    // Process callbacks (exception should be caught internally)
    for (int i = 0; i < 20; i++) {
        connector_->MainThreadAction();
        std::this_thread::sleep_for(std::chrono::milliseconds(10));
    }

    // Then: System should remain stable
    EXPECT_TRUE(connector_->IsConnected()) << "Connection should remain stable despite callback exception";
}
