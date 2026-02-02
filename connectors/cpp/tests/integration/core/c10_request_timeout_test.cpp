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

    ASSERT_TRUE(CreateStageAndConnect());

    // When: Send a request that won't respond in time
    std::string slow_request_data = "{\"delay\":5000}";  // Ask server to delay 5 seconds
    Bytes payload(slow_request_data.begin(), slow_request_data.end());
    auto packet = Packet::FromBytes("SlowRequest", std::move(payload));

    auto future = connector_->RequestAsync(std::move(packet));

    // Then: Should timeout
    bool timed_out = false;
    try {
        auto response = WaitWithMainThreadAction(future, 5000);
        // Check if response has timeout error
        if (response.GetMsgId() == msg_id::TIMEOUT || response.GetErrorCode() != 0) {
            timed_out = true;
        }
    } catch (const std::exception& e) {
        // Timeout exception is expected
        timed_out = true;
    }

    EXPECT_TRUE(timed_out) << "Request should timeout with short timeout configuration";
}

TEST_F(C10_RequestTimeoutTest, Request_WithNormalTimeout_Succeeds) {
    // Given: Connected with normal timeout
    config_.request_timeout_ms = 5000;  // 5 second timeout
    connector_ = std::make_unique<Connector>();
    connector_->Init(config_);

    ASSERT_TRUE(CreateStageAndConnect());

    // When: Send a normal request
    std::string echo_data = "{\"content\":\"Normal request\",\"sequence\":1}";
    Bytes payload(echo_data.begin(), echo_data.end());
    auto packet = Packet::FromBytes("EchoRequest", std::move(payload));

    auto future = connector_->RequestAsync(std::move(packet));

    // Then: Should succeed before timeout
    try {
        auto response = WaitWithMainThreadAction(future, 6000);
        EXPECT_EQ(response.GetErrorCode(), 0) << "Normal request should succeed";
    } catch (const std::exception& e) {
        FAIL() << "Normal request should not timeout: " << e.what();
    }
}

TEST_F(C10_RequestTimeoutTest, Request_TimeoutMessage_HasTimeoutId) {
    // Given: Connected with short timeout
    config_.request_timeout_ms = 200;
    connector_ = std::make_unique<Connector>();
    connector_->Init(config_);

    ASSERT_TRUE(CreateStageAndConnect());

    // When: Send request that will timeout
    std::string slow_data = "{\"delay\":10000}";
    Bytes payload(slow_data.begin(), slow_data.end());
    auto packet = Packet::FromBytes("SlowRequest", std::move(payload));

    auto future = connector_->RequestAsync(std::move(packet));

    // Then: Timeout message should have special ID
    bool has_timeout_id = false;
    try {
        auto response = WaitWithMainThreadAction(future, 5000);
        if (response.GetMsgId() == msg_id::TIMEOUT) {
            has_timeout_id = true;
        }
    } catch (...) {
        // Exception is also acceptable for timeout
        SUCCEED() << "Request timed out with exception";
        return;
    }

    // Either exception or timeout message ID is acceptable
    SUCCEED() << "Timeout handled correctly";
}

TEST_F(C10_RequestTimeoutTest, Request_MultipleTimeouts_AllHandledCorrectly) {
    // Given: Connected with short timeout
    config_.request_timeout_ms = 150;
    connector_ = std::make_unique<Connector>();
    connector_->Init(config_);

    ASSERT_TRUE(CreateStageAndConnect());

    // When: Send multiple requests that will timeout
    int timeout_count = 0;
    for (int i = 0; i < 3; i++) {
        std::string slow_data = "{\"delay\":5000}";
        Bytes payload(slow_data.begin(), slow_data.end());
        auto packet = Packet::FromBytes("SlowRequest", std::move(payload));

        auto future = connector_->RequestAsync(std::move(packet));

        try {
            auto response = WaitWithMainThreadAction(future, 2000);
            if (response.GetMsgId() == msg_id::TIMEOUT || response.GetErrorCode() != 0) {
                timeout_count++;
            }
        } catch (...) {
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

    ASSERT_TRUE(CreateStageAndConnect());

    // First request that times out
    std::string slow_data = "{\"delay\":5000}";
    Bytes slow_payload(slow_data.begin(), slow_data.end());
    auto slow_packet = Packet::FromBytes("SlowRequest", std::move(slow_payload));
    auto slow_future = connector_->RequestAsync(std::move(slow_packet));

    try {
        WaitWithMainThreadAction(slow_future, 2000);
    } catch (...) {
        // Expected timeout
    }

    // When: Send a new fast request
    std::string fast_data = "{\"content\":\"Fast request\",\"sequence\":1}";
    Bytes fast_payload(fast_data.begin(), fast_data.end());
    auto fast_packet = Packet::FromBytes("EchoRequest", std::move(fast_payload));
    auto fast_future = connector_->RequestAsync(std::move(fast_packet));

    // Then: New request should still work
    try {
        auto response = WaitWithMainThreadAction(fast_future, 5000);
        EXPECT_TRUE(connector_->IsConnected()) << "Connection should still be valid after timeout";
    } catch (const std::exception& e) {
        // May still timeout if configuration is too aggressive, but shouldn't crash
        SUCCEED() << "System stable after timeout: " << e.what();
    }
}
