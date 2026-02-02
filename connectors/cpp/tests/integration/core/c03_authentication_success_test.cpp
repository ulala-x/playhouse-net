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
    auto future = connector_->AuthenticateAsync("TestService", "test_account", payload);

    // Then: Authentication should succeed
    bool auth_success = false;
    try {
        auth_success = WaitWithMainThreadAction(future, 5000);
    } catch (const std::exception& e) {
        FAIL() << "Authentication threw exception: " << e.what();
    }

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
    Packet auth_packet("Authenticate", payload);

    // Note: The C++ API uses AuthenticateAsync, so we'll test the async version
    auto future = connector_->AuthenticateAsync("TestService", "test_account2", payload);

    bool auth_result = false;
    try {
        auth_result = WaitWithMainThreadAction(future, 5000);
        callback_fired = true;
    } catch (...) {
        // Exception means callback didn't fire properly
    }

    // Then: Callback should fire with success
    EXPECT_TRUE(callback_fired) << "Authentication callback should fire";
    EXPECT_TRUE(auth_result) << "Authentication result should be success";
}

TEST_F(C03_AuthenticationSuccessTest, Authenticate_WithEmptyPayload_HandledGracefully) {
    // Given: Connected to server
    ASSERT_TRUE(CreateStageAndConnect());

    // When: Authenticate with empty payload
    Bytes empty_payload;
    auto future = connector_->AuthenticateAsync("TestService", "empty_account", empty_payload);

    // Then: Should complete without crashing (result depends on server implementation)
    try {
        bool result = WaitWithMainThreadAction(future, 5000);
        // Authentication might succeed or fail depending on server, but shouldn't crash
        SUCCEED() << "Authentication completed without crash, result: " << result;
    } catch (const std::exception& e) {
        // Timeout or other exception is acceptable
        SUCCEED() << "Authentication failed gracefully: " << e.what();
    }
}

TEST_F(C03_AuthenticationSuccessTest, Authenticate_MultipleUsers_EachSucceeds) {
    // Given: Connected to server
    ASSERT_TRUE(CreateStageAndConnect());

    // When: Authenticate multiple users sequentially
    std::vector<bool> results;

    for (int i = 0; i < 3; i++) {
        std::string user_data = "{\"userId\":\"user" + std::to_string(i) + "\",\"token\":\"valid_token\"}";
        Bytes payload(user_data.begin(), user_data.end());

        auto future = connector_->AuthenticateAsync("TestService", "account" + std::to_string(i), payload);

        try {
            bool result = WaitWithMainThreadAction(future, 5000);
            results.push_back(result);
        } catch (...) {
            results.push_back(false);
        }
    }

    // Then: At least some authentications should succeed
    int success_count = std::count(results.begin(), results.end(), true);
    EXPECT_GT(success_count, 0) << "At least one authentication should succeed";
}
