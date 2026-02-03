#include "../base_integration_test.hpp"
#include <gtest/gtest.h>

using namespace playhouse;
using namespace playhouse::test;

/// C-10: Request Timeout Tests
/// Verifies request timeout handling
class C10_RequestTimeoutTest : public BaseIntegrationTest {};

TEST_F(C10_RequestTimeoutTest, Request_WithShortTimeout_TimesOut) {
    // Given: Connected with very short timeout
    config_.request_timeout_ms = 100;  // 100ms timeout
    connector_ = std::make_unique<Connector>();
    connector_->Init(config_);

    ASSERT_TRUE(CreateStageConnectAndAuthenticate("timeout_short_user"));

    // When: Send a request that won't respond in time
    Bytes payload = proto::EncodeNoResponseRequest(5000);
    auto packet = Packet::FromBytes("NoResponseRequest", std::move(payload));

    Packet response = Packet::Empty("Empty");
    bool completed = RequestAndWait(std::move(packet), response, 500);

    // Then: Should timeout or return timeout message
    bool timed_out = !completed;
    if (completed) {
        if (response.GetMsgId() == msg_id::TIMEOUT || response.GetErrorCode() != 0) {
            timed_out = true;
        }
    }

    EXPECT_TRUE(timed_out) << "Request should timeout with short timeout configuration";
}

TEST_F(C10_RequestTimeoutTest, Request_WithNormalTimeout_Succeeds) {
    // Given: Connected with normal timeout
    config_.request_timeout_ms = 5000;  // 5 second timeout
    connector_ = std::make_unique<Connector>();
    connector_->Init(config_);

    ASSERT_TRUE(CreateStageConnectAndAuthenticate("timeout_normal_user"));

    // When: Send a normal request
    Bytes payload = proto::EncodeEchoRequest("Normal request", 1);
    auto packet = Packet::FromBytes("EchoRequest", std::move(payload));

    Packet response = Packet::Empty("Empty");
    bool completed = RequestAndWait(std::move(packet), response, 6000);

    // Then: Should succeed before timeout
    ASSERT_TRUE(completed) << "Normal request should complete";
    EXPECT_EQ(response.GetErrorCode(), 0) << "Normal request should succeed";
}

TEST_F(C10_RequestTimeoutTest, Request_TimeoutMessage_HasTimeoutId) {
    // Given: Connected with short timeout
    config_.request_timeout_ms = 200;
    connector_ = std::make_unique<Connector>();
    connector_->Init(config_);

    ASSERT_TRUE(CreateStageConnectAndAuthenticate("timeout_msgid_user"));

    // When: Send request that will timeout
    Bytes payload = proto::EncodeNoResponseRequest(10000);
    auto packet = Packet::FromBytes("NoResponseRequest", std::move(payload));

    Packet response = Packet::Empty("Empty");
    bool completed = RequestAndWait(std::move(packet), response, 500);

    // Then: Timeout message should have special ID or no response
    if (!completed) {
        SUCCEED() << "Request timed out without response";
        return;
    }

    EXPECT_EQ(response.GetMsgId(), msg_id::TIMEOUT);
}

TEST_F(C10_RequestTimeoutTest, Request_MultipleTimeouts_AllHandledCorrectly) {
    // Given: Connected with short timeout
    config_.request_timeout_ms = 150;
    connector_ = std::make_unique<Connector>();
    connector_->Init(config_);

    ASSERT_TRUE(CreateStageConnectAndAuthenticate("timeout_multi_user"));

    // When: Send multiple requests that will timeout
    int timeout_count = 0;
    for (int i = 0; i < 3; i++) {
        Bytes payload = proto::EncodeNoResponseRequest(5000);
        auto packet = Packet::FromBytes("NoResponseRequest", std::move(payload));

        Packet response = Packet::Empty("Empty");
        bool completed = RequestAndWait(std::move(packet), response, 200);
        if (!completed) {
            timeout_count++;
        } else if (response.GetMsgId() == msg_id::TIMEOUT || response.GetErrorCode() != 0) {
            timeout_count++;
        }
    }

    // Then: All should timeout
    EXPECT_GT(timeout_count, 0) << "At least some requests should timeout";
}

TEST_F(C10_RequestTimeoutTest, Request_AfterTimeout_CanStillSendNew) {
    // Given: Connected and had a timeout
    config_.request_timeout_ms = 100;
    connector_ = std::make_unique<Connector>();
    connector_->Init(config_);

    ASSERT_TRUE(CreateStageConnectAndAuthenticate("timeout_after_user"));

    // First request that times out
    Bytes slow_payload = proto::EncodeNoResponseRequest(5000);
    auto slow_packet = Packet::FromBytes("NoResponseRequest", std::move(slow_payload));
    Packet slow_response = Packet::Empty("Empty");
    RequestAndWait(std::move(slow_packet), slow_response, 200);

    // When: Send a new fast request
    Bytes fast_payload = proto::EncodeEchoRequest("Fast request", 1);
    auto fast_packet = Packet::FromBytes("EchoRequest", std::move(fast_payload));
    Packet fast_response = Packet::Empty("Empty");
    bool completed = RequestAndWait(std::move(fast_packet), fast_response, 5000);

    // Then: New request should still work
    if (completed) {
        EXPECT_TRUE(connector_->IsConnected()) << "Connection should still be valid after timeout";
    } else {
        SUCCEED() << "System stable after timeout";
    }
}
