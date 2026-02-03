#include "../base_integration_test.hpp"
#include <gtest/gtest.h>
#include <algorithm>
#include <optional>
#include <vector>

using namespace playhouse;
using namespace playhouse::test;

/// A-02: Large Payload Tests
/// Verifies handling of large message payloads and compression behavior
class A02_LargePayloadTest : public BaseIntegrationTest {
protected:
    void SetUp() override {
        BaseIntegrationTest::SetUp();

        config_.send_buffer_size = 2 * 1024 * 1024;
        config_.receive_buffer_size = 2 * 1024 * 1024;
        config_.request_timeout_ms = 30000;

        connector_ = std::make_unique<Connector>();
        connector_->Init(config_);
    }
};

TEST_F(A02_LargePayloadTest, LargePayload_1MB_Received) {
    // Given: Connected to server
    ASSERT_TRUE(CreateStageConnectAndAuthenticate("large_payload_user"));

    // When: Request large payload (server returns 1MB)
    Bytes payload = proto::EncodeLargePayloadRequest(1048576);
    auto packet = Packet::FromBytes("LargePayloadRequest", std::move(payload));

    Packet response = Packet::Empty("Empty");
    bool completed = RequestAndWait(std::move(packet), response, 30000);

    // Then: Should receive 1MB payload
    ASSERT_TRUE(completed) << "Large payload request should complete";
    Bytes reply_payload;
    ASSERT_TRUE(proto::DecodeBenchmarkReplyPayload(response.GetPayload(), reply_payload));
    EXPECT_EQ(reply_payload.size(), 1048576U);
}

TEST_F(A02_LargePayloadTest, LargePayload_DataIntegrity) {
    // Given: Connected to server
    ASSERT_TRUE(CreateStageConnectAndAuthenticate("large_payload_integrity_user"));

    // When: Request large payload
    Bytes payload = proto::EncodeLargePayloadRequest(1048576);
    auto packet = Packet::FromBytes("LargePayloadRequest", std::move(payload));

    Packet response = Packet::Empty("Empty");
    bool completed = RequestAndWait(std::move(packet), response, 30000);

    // Then: Payload should keep byte pattern
    ASSERT_TRUE(completed);
    Bytes reply_payload;
    ASSERT_TRUE(proto::DecodeBenchmarkReplyPayload(response.GetPayload(), reply_payload));
    ASSERT_GE(reply_payload.size(), 1000U);
    for (size_t i = 0; i < 1000; i++) {
        EXPECT_EQ(reply_payload[i], static_cast<uint8_t>(i % 256));
    }
}

TEST_F(A02_LargePayloadTest, LargePayload_Sequential_Requests) {
    // Given: Connected to server
    ASSERT_TRUE(CreateStageConnectAndAuthenticate("large_payload_seq_user"));

    // When: Send multiple large payload requests sequentially
    for (int i = 0; i < 3; i++) {
        Bytes payload = proto::EncodeLargePayloadRequest(1048576);
        auto packet = Packet::FromBytes("LargePayloadRequest", std::move(payload));

        Packet response = Packet::Empty("Empty");
        bool completed = RequestAndWait(std::move(packet), response, 30000);
        ASSERT_TRUE(completed);
        Bytes reply_payload;
        ASSERT_TRUE(proto::DecodeBenchmarkReplyPayload(response.GetPayload(), reply_payload));
        EXPECT_EQ(reply_payload.size(), 1048576U);
    }
}

TEST_F(A02_LargePayloadTest, LargePayload_Send_Large_Request) {
    // Given: Connected to server
    ASSERT_TRUE(CreateStageConnectAndAuthenticate("large_payload_send_user"));

    // When: Send large request payload via EchoRequest
    std::string large_content(100000, 'A');  // 100KB
    Bytes payload = proto::EncodeEchoRequest(large_content, 1);
    auto packet = Packet::FromBytes("EchoRequest", std::move(payload));

    Packet response = Packet::Empty("Empty");
    bool completed = RequestAndWait(std::move(packet), response, 10000);

    // Then: Echo should return the same content
    ASSERT_TRUE(completed);
    std::string echo_content;
    int32_t echo_seq = 0;
    ASSERT_TRUE(proto::DecodeEchoReply(response.GetPayload(), echo_content, echo_seq));
    EXPECT_EQ(echo_content, large_content);
}

TEST_F(A02_LargePayloadTest, LargePayload_Parallel_Requests) {
    // Given: Connected to server
    ASSERT_TRUE(CreateStageConnectAndAuthenticate("large_payload_parallel_user"));

    const int request_count = 3;
    std::vector<bool> completed(request_count, false);
    std::vector<std::optional<Packet>> responses(request_count);

    for (int i = 0; i < request_count; i++) {
        Bytes payload = proto::EncodeLargePayloadRequest(524288);
        auto packet = Packet::FromBytes("LargePayloadRequest", std::move(payload));
        connector_->Request(std::move(packet), [&, i](Packet response) {
            responses[i] = std::move(response);
            completed[i] = true;
        });
    }

    auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(30);
    while (std::chrono::steady_clock::now() < deadline) {
        connector_->MainThreadAction();
        if (std::all_of(completed.begin(), completed.end(), [](bool done) { return done; })) {
            break;
        }
        std::this_thread::sleep_for(std::chrono::milliseconds(10));
    }

    ASSERT_TRUE(std::all_of(completed.begin(), completed.end(), [](bool done) { return done; }));
    for (int i = 0; i < request_count; i++) {
        ASSERT_TRUE(responses[i].has_value());
        Bytes reply_payload;
        ASSERT_TRUE(proto::DecodeBenchmarkReplyPayload(responses[i]->GetPayload(), reply_payload));
        EXPECT_EQ(reply_payload.size(), 1048576U);
    }
}
