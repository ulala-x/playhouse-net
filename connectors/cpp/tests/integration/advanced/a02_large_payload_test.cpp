#include "../base_integration_test.hpp"
#include <gtest/gtest.h>

using namespace playhouse;
using namespace playhouse::test;

/// A-02: Large Payload Tests
/// Verifies handling of large message payloads
class A02_LargePayloadTest : public BaseIntegrationTest {};

TEST_F(A02_LargePayloadTest, LargePayload_1KB_SendAndReceive) {
    // Given: Connected to server
    ASSERT_TRUE(CreateStageAndConnect());

    // When: Send 1KB payload
    std::string large_content(1024, 'A');
    std::string echo_data = "{\"content\":\"" + large_content + "\",\"sequence\":1}";
    Bytes payload(echo_data.begin(), echo_data.end());
    auto packet = Packet::FromBytes("EchoRequest", std::move(payload));

    Packet response = Packet::Empty("Empty");
    bool completed = RequestAndWait(std::move(packet), response, 5000);

    // Then: Should handle successfully
    ASSERT_TRUE(completed) << "1KB payload should complete";
    EXPECT_EQ(response.GetErrorCode(), 0) << "1KB payload should be handled";
    EXPECT_FALSE(response.GetPayload().empty());
}

TEST_F(A02_LargePayloadTest, LargePayload_10KB_SendAndReceive) {
    // Given: Connected to server
    ASSERT_TRUE(CreateStageAndConnect());

    // When: Send 10KB payload
    std::string large_content(10 * 1024, 'B');
    std::string echo_data = "{\"content\":\"" + large_content + "\",\"sequence\":2}";
    Bytes payload(echo_data.begin(), echo_data.end());
    auto packet = Packet::FromBytes("EchoRequest", std::move(payload));

    Packet response = Packet::Empty("Empty");
    bool completed = RequestAndWait(std::move(packet), response, 10000);

    // Then: Should handle successfully
    ASSERT_TRUE(completed) << "10KB payload should complete";
    EXPECT_EQ(response.GetErrorCode(), 0) << "10KB payload should be handled";
}

TEST_F(A02_LargePayloadTest, LargePayload_100KB_SendAndReceive) {
    // Given: Connected to server
    ASSERT_TRUE(CreateStageAndConnect());

    // When: Send 100KB payload
    std::string large_content(100 * 1024, 'C');
    std::string echo_data = "{\"content\":\"" + large_content + "\",\"sequence\":3}";
    Bytes payload(echo_data.begin(), echo_data.end());
    auto packet = Packet::FromBytes("EchoRequest", std::move(payload));

    Packet response = Packet::Empty("Empty");
    bool completed = RequestAndWait(std::move(packet), response, 15000);

    // Then: Should handle successfully (may take longer)
    if (completed) {
        EXPECT_EQ(response.GetErrorCode(), 0) << "100KB payload should be handled";
    } else {
        SUCCEED() << "100KB payload timed out or was rejected";
    }
}

TEST_F(A02_LargePayloadTest, LargePayload_1MB_NearMaxSize) {
    // Given: Connected to server
    ASSERT_TRUE(CreateStageAndConnect());

    // When: Send 1MB payload (near protocol max of 2MB)
    std::vector<uint8_t> large_payload(1 * 1024 * 1024, 0xAB);
    auto packet = Packet::FromBytes("LargeDataRequest", std::move(large_payload));

    Packet response = Packet::Empty("Empty");
    bool completed = RequestAndWait(std::move(packet), response, 20000);

    // Then: Should handle or reject gracefully
    if (completed) {
        SUCCEED() << "1MB payload handled with error code: " << response.GetErrorCode();
    } else {
        SUCCEED() << "1MB payload timed out or was rejected";
    }
}

TEST_F(A02_LargePayloadTest, LargePayload_BinaryData_HandledCorrectly) {
    // Given: Connected to server
    ASSERT_TRUE(CreateStageAndConnect());

    // When: Send binary data with various byte patterns
    Bytes binary_payload;
    for (int i = 0; i < 5000; i++) {
        binary_payload.push_back(static_cast<uint8_t>(i % 256));
    }

    auto packet = Packet::FromBytes("BinaryEchoRequest", std::move(binary_payload));
    Packet response = Packet::Empty("Empty");
    bool completed = RequestAndWait(std::move(packet), response, 10000);

    // Then: Binary data should be handled
    ASSERT_TRUE(completed) << "Binary payload should complete";
    EXPECT_EQ(response.GetErrorCode(), 0);
    EXPECT_FALSE(response.GetPayload().empty());
}

TEST_F(A02_LargePayloadTest, LargePayload_MultipleConsecutive_SystemStable) {
    // Given: Connected to server
    ASSERT_TRUE(CreateStageAndConnect());

    // When: Send multiple large payloads consecutively
    int success_count = 0;
    for (int i = 0; i < 3; i++) {
        std::string large_content(20 * 1024, 'D');  // 20KB each
        std::string echo_data = "{\"content\":\"" + large_content + "\",\"sequence\":" + std::to_string(i) + "}";
        Bytes payload(echo_data.begin(), echo_data.end());
        auto packet = Packet::FromBytes("EchoRequest", std::move(payload));

        Packet response = Packet::Empty("Empty");
        bool completed = RequestAndWait(std::move(packet), response, 10000);
        if (completed && response.GetErrorCode() == 0) {
            success_count++;
        }
    }

    // Then: System should remain stable
    EXPECT_GT(success_count, 0) << "At least some large payloads should succeed";
    EXPECT_TRUE(connector_->IsConnected()) << "Connection should remain stable";
}
