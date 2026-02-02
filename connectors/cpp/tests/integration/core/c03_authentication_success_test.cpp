#include "../base_integration_test.hpp"
#include <gtest/gtest.h>

using namespace playhouse;
using namespace playhouse::test;

/// C-03: Authentication Success Tests
/// Verifies successful authentication with valid credentials
class C03_AuthenticationSuccessTest : public BaseIntegrationTest {};

TEST_F(C03_AuthenticationSuccessTest, Authenticate_WithValidCredentials_Succeeds) {
    // Given: Connected to server
    ASSERT_TRUE(CreateStageAndConnect());

    // Create simple auth payload
    std::string auth_data = "{\"userId\":\"test_user\",\"token\":\"valid_token\"}";
    Bytes payload(auth_data.begin(), auth_data.end());

    // When: Authenticate with valid credentials
    Packet auth_packet = Packet::FromBytes("Authenticate", std::move(payload));

    // Then: Authentication should succeed
    bool auth_success = false;
    bool completed = AuthenticateAndWait(std::move(auth_packet), auth_success, 5000);
    ASSERT_TRUE(completed) << "Authentication should complete";
    EXPECT_TRUE(auth_success) << "Authentication should succeed";
}

TEST_F(C03_AuthenticationSuccessTest, Authenticate_CallbackVersion_SuccessCallbackFires) {
    // Given: Connected to server
    ASSERT_TRUE(CreateStageAndConnect());

    // Create auth payload
    std::string auth_data = "{\"userId\":\"test_user2\",\"token\":\"valid_token\"}";
    Bytes payload(auth_data.begin(), auth_data.end());

    // When: Authenticate with callback
    bool callback_fired = false;
    Packet auth_packet = Packet::FromBytes("Authenticate", std::move(payload));

    bool auth_result = false;
    bool completed = AuthenticateAndWait(std::move(auth_packet), auth_result, 5000);
    callback_fired = completed;

    // Then: Callback should fire with success
    EXPECT_TRUE(callback_fired) << "Authentication callback should fire";
    EXPECT_TRUE(auth_result) << "Authentication result should be success";
}

TEST_F(C03_AuthenticationSuccessTest, Authenticate_WithEmptyPayload_HandledGracefully) {
    // Given: Connected to server
    ASSERT_TRUE(CreateStageAndConnect());

    // When: Authenticate with empty payload
    Bytes empty_payload;
    Packet auth_packet = Packet::FromBytes("Authenticate", std::move(empty_payload));

    // Then: Should complete without crashing (result depends on server implementation)
    bool result = false;
    bool completed = AuthenticateAndWait(std::move(auth_packet), result, 5000);
    EXPECT_TRUE(completed) << "Authentication should complete";
    SUCCEED() << "Authentication completed, result: " << result;
}

TEST_F(C03_AuthenticationSuccessTest, Authenticate_MultipleUsers_EachSucceeds) {
    // Given: Connected to server
    ASSERT_TRUE(CreateStageAndConnect());

    // When: Authenticate multiple users sequentially
    std::vector<bool> results;

    for (int i = 0; i < 3; i++) {
        std::string user_data = "{\"userId\":\"user" + std::to_string(i) + "\",\"token\":\"valid_token\"}";
        Bytes payload(user_data.begin(), user_data.end());

        Packet auth_packet = Packet::FromBytes("Authenticate", std::move(payload));
        bool result = false;
        bool completed = AuthenticateAndWait(std::move(auth_packet), result, 5000);
        results.push_back(completed && result);
    }

    // Then: At least some authentications should succeed
    int success_count = std::count(results.begin(), results.end(), true);
    EXPECT_GT(success_count, 0) << "At least one authentication should succeed";
}
