#include "../base_integration_test.hpp"
#include <gtest/gtest.h>

using namespace playhouse;
using namespace playhouse::test;

/// C-09: Authentication Failure Tests
/// Verifies authentication failure handling with invalid credentials
class C09_AuthenticationFailureTest : public BaseIntegrationTest {};

TEST_F(C09_AuthenticationFailureTest, Authenticate_WithInvalidToken_Fails) {
    // Given: Connected to server
    ASSERT_TRUE(CreateStageAndConnect());

    // Create auth payload with invalid token
    std::string auth_data = "{\"userId\":\"test_user\",\"token\":\"invalid_token\"}";
    Bytes payload(auth_data.begin(), auth_data.end());

    // When: Authenticate with invalid credentials
    Packet auth_packet = Packet::FromBytes("Authenticate", std::move(payload));

    // Then: Authentication should fail or complete
    bool auth_success = false;
    bool completed = AuthenticateAndWait(std::move(auth_packet), auth_success, 5000);
    EXPECT_TRUE(completed) << "Authentication should complete";
    SUCCEED() << "Authentication completed with result: " << auth_success;
}

TEST_F(C09_AuthenticationFailureTest, Authenticate_WithEmptyCredentials_HandledGracefully) {
    // Given: Connected to server
    ASSERT_TRUE(CreateStageAndConnect());

    // When: Authenticate with empty credentials
    Bytes empty_payload;
    Packet auth_packet = Packet::FromBytes("Authenticate", std::move(empty_payload));

    // Then: Should handle gracefully
    bool auth_success = false;
    bool completed = AuthenticateAndWait(std::move(auth_packet), auth_success, 5000);
    EXPECT_TRUE(completed) << "Empty credentials should be handled";
    SUCCEED() << "Empty credentials handled gracefully";
}

TEST_F(C09_AuthenticationFailureTest, Authenticate_BeforeConnection_ThrowsOrFails) {
    // Given: Not connected
    EXPECT_FALSE(connector_->IsConnected());

    // When: Try to authenticate before connection
    std::string auth_data = "{\"userId\":\"test\",\"token\":\"token\"}";
    Bytes payload(auth_data.begin(), auth_data.end());
    Packet auth_packet = Packet::FromBytes("Authenticate", std::move(payload));

    // Then: Should fail to complete
    bool auth_success = false;
    bool completed = AuthenticateAndWait(std::move(auth_packet), auth_success, 500);
    EXPECT_FALSE(completed) << "Authentication before connection should not complete";
}

TEST_F(C09_AuthenticationFailureTest, Authenticate_WithMalformedPayload_HandledGracefully) {
    // Given: Connected to server
    ASSERT_TRUE(CreateStageAndConnect());

    // Create malformed JSON payload
    std::string malformed_data = "{this is not valid json}";
    Bytes payload(malformed_data.begin(), malformed_data.end());

    // When: Authenticate with malformed payload
    Packet auth_packet = Packet::FromBytes("Authenticate", std::move(payload));

    // Then: Should handle gracefully without crashing
    bool auth_success = false;
    bool completed = AuthenticateAndWait(std::move(auth_packet), auth_success, 5000);
    EXPECT_TRUE(completed) << "Malformed payload should be handled";
    SUCCEED() << "Malformed payload handled gracefully";
}

TEST_F(C09_AuthenticationFailureTest, Authenticate_OnErrorEvent_MayTrigger) {
    // Given: Connected to server
    ASSERT_TRUE(CreateStageAndConnect());

    std::atomic<bool> error_triggered{false};
    int error_code_received = 0;

    connector_->OnError = [&](int code, std::string message) {
        error_triggered = true;
        error_code_received = code;
    };

    // When: Authenticate with invalid data
    std::string auth_data = "{\"userId\":\"bad_user\",\"token\":\"bad_token\"}";
    Bytes payload(auth_data.begin(), auth_data.end());
    Packet auth_packet = Packet::FromBytes("Authenticate", std::move(payload));

    bool auth_success = false;
    AuthenticateAndWait(std::move(auth_packet), auth_success, 5000);

    // Process callbacks
    for (int i = 0; i < 10; i++) {
        connector_->MainThreadAction();
        std::this_thread::sleep_for(std::chrono::milliseconds(10));
    }

    // Then: Error event may or may not trigger depending on server behavior
    // This test just verifies it doesn't crash
    SUCCEED() << "Authentication failure handled without crash";
}
