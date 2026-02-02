#include "../base_integration_test.hpp"
#include <gtest/gtest.h>

using namespace playhouse;
using namespace playhouse::test;

/// C-05: Echo Request-Response Tests
/// Verifies request-response pattern using echo messages
class C05_EchoRequestResponseTest : public BaseIntegrationTest {};

TEST_F(C05_EchoRequestResponseTest, Echo_SimpleMessage_ResponseReceived) {
    // Given: Connected and authenticated
    ASSERT_TRUE(CreateStageAndConnect());

    // Create echo request payload
    std::string echo_data = "{\"content\":\"Hello World\",\"sequence\":1}";
    Bytes payload(echo_data.begin(), echo_data.end());
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
    ASSERT_TRUE(CreateStageAndConnect());

    // Create echo request
    std::string echo_data = "{\"content\":\"Callback Test\",\"sequence\":2}";
    Bytes payload(echo_data.begin(), echo_data.end());
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
    ASSERT_TRUE(CreateStageAndConnect());

    // When: Send multiple echo requests sequentially
    for (int i = 0; i < 5; i++) {
        std::string echo_data = "{\"content\":\"Message" + std::to_string(i) + "\",\"sequence\":" + std::to_string(i) + "}";
        Bytes payload(echo_data.begin(), echo_data.end());
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
    ASSERT_TRUE(CreateStageAndConnect());

    // Create large echo content (1KB)
    std::string large_content(1000, 'A');
    std::string echo_data = "{\"content\":\"" + large_content + "\",\"sequence\":99}";
    Bytes payload(echo_data.begin(), echo_data.end());
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
    ASSERT_TRUE(CreateStageAndConnect());

    // Create echo with special characters
    std::string echo_data = "{\"content\":\"Hello 世界 🌍\",\"sequence\":42}";
    Bytes payload(echo_data.begin(), echo_data.end());
    auto packet = Packet::FromBytes("EchoRequest", std::move(payload));

    // When: Send echo request
    Packet response = Packet::Empty("Empty");
    bool completed = RequestAndWait(std::move(packet), response, 5000);

    // Then: Response should be received
    ASSERT_TRUE(completed) << "Echo with special characters should complete";
    EXPECT_EQ(response.GetErrorCode(), 0);
}
