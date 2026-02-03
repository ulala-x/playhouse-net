#include "../base_integration_test.hpp"
#include <gtest/gtest.h>

using namespace playhouse;
using namespace playhouse::test;

/// C-05: Echo Request-Response Tests
/// Verifies request-response pattern using echo messages
class C05_EchoRequestResponseTest : public BaseIntegrationTest {};

TEST_F(C05_EchoRequestResponseTest, Echo_SimpleMessage_ResponseReceived) {
    // Given: Connected and authenticated
    ASSERT_TRUE(CreateStageConnectAndAuthenticate());

    // Create echo request payload
    Bytes payload = proto::EncodeEchoRequest("Hello World", 1);
    auto packet = Packet::FromBytes("EchoRequest", std::move(payload));

    // When: Send echo request
    Packet response = Packet::Empty("Empty");
    bool completed = RequestAndWait(std::move(packet), response, 5000);

    // Then: Response should be received
    ASSERT_TRUE(completed) << "Echo request should complete";
    EXPECT_EQ(response.GetMsgId(), "EchoReply");
    EXPECT_EQ(response.GetErrorCode(), 0) << "Response should not have error code";
    EXPECT_FALSE(response.GetPayload().empty()) << "Response should have payload";
}

TEST_F(C05_EchoRequestResponseTest, Echo_RequestWithCallback_CallbackFires) {
    // Given: Connected and authenticated
    ASSERT_TRUE(CreateStageConnectAndAuthenticate());

    // Create echo request
    Bytes payload = proto::EncodeEchoRequest("Callback Test", 2);
    auto packet = Packet::FromBytes("EchoRequest", std::move(payload));

    // When: Send request with callback
    bool callback_fired = false;
    std::string received_msg_id;

    connector_->Request(std::move(packet), [&](Packet response) {
        callback_fired = true;
        received_msg_id = response.GetMsgId();
    });

    // Wait for callback
    bool completed = WaitForConditionWithMainThreadAction([&]() {
        return callback_fired;
    }, 5000);

    // Then: Callback should fire with response
    EXPECT_TRUE(completed) << "Callback should fire within timeout";
    EXPECT_TRUE(callback_fired) << "Callback should be fired";
    EXPECT_EQ(received_msg_id, "EchoReply") << "Response message ID should be EchoReply";
}

TEST_F(C05_EchoRequestResponseTest, Echo_MultipleSequential_AllSucceed) {
    // Given: Connected and authenticated
    ASSERT_TRUE(CreateStageConnectAndAuthenticate());

    // When: Send multiple echo requests sequentially
    for (int i = 0; i < 5; i++) {
        Bytes payload = proto::EncodeEchoRequest("Message" + std::to_string(i), i);
        auto packet = Packet::FromBytes("EchoRequest", std::move(payload));

        Packet response = Packet::Empty("Empty");
        bool completed = RequestAndWait(std::move(packet), response, 5000);
        EXPECT_TRUE(completed) << "Echo request " << i << " should complete";
        if (completed) {
            EXPECT_EQ(response.GetErrorCode(), 0) << "Echo " << i << " should succeed";
        }
    }

    // Then: All requests should succeed (verified in loop)
    SUCCEED();
}

TEST_F(C05_EchoRequestResponseTest, Echo_WithLargeContent_ResponseReceived) {
    // Given: Connected and authenticated
    ASSERT_TRUE(CreateStageConnectAndAuthenticate());

    // Create large echo content (1KB)
    std::string large_content(1000, 'A');
    Bytes payload = proto::EncodeEchoRequest(large_content, 99);
    auto packet = Packet::FromBytes("EchoRequest", std::move(payload));

    // When: Send large echo request
    Packet response = Packet::Empty("Empty");
    bool completed = RequestAndWait(std::move(packet), response, 5000);

    // Then: Response should be received
    ASSERT_TRUE(completed) << "Large echo request should complete";
    EXPECT_EQ(response.GetErrorCode(), 0);
    EXPECT_FALSE(response.GetPayload().empty());
}

TEST_F(C05_EchoRequestResponseTest, Echo_WithSpecialCharacters_HandledCorrectly) {
    // Given: Connected and authenticated
    ASSERT_TRUE(CreateStageConnectAndAuthenticate());

    // Create echo with special characters
    std::string echo_data = "Hello \xEC\x84\xB8\xEA\xB3\x84 \xF0\x9F\x8C\x8D";
    Bytes payload = proto::EncodeEchoRequest(echo_data, 42);
    auto packet = Packet::FromBytes("EchoRequest", std::move(payload));

    // When: Send echo request
    Packet response = Packet::Empty("Empty");
    bool completed = RequestAndWait(std::move(packet), response, 5000);

    // Then: Response should be received
    ASSERT_TRUE(completed) << "Echo with special characters should complete";
    EXPECT_EQ(response.GetErrorCode(), 0);
}
