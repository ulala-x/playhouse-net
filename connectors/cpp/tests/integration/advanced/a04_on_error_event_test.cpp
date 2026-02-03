#include "../base_integration_test.hpp"
#include <gtest/gtest.h>
#include <atomic>

using namespace playhouse;
using namespace playhouse::test;

/// A-04: OnError Event Tests
/// Verifies OnError event callback mechanism
class A04_OnErrorEventTest : public BaseIntegrationTest {};

TEST_F(A04_OnErrorEventTest, OnError_ConnectionClosed_EventFires) {
    // Given: Connected to server with OnError handler
    ASSERT_TRUE(CreateStageAndConnect());

    std::atomic<bool> error_fired{false};
    int error_code_received = 0;
    std::string error_message;

    connector_->OnError = [&](int code, std::string message) {
        error_fired = true;
        error_code_received = code;
        error_message = message;
    };

    // When: Force connection error by disconnecting and attempting operation
    connector_->Disconnect();
    std::this_thread::sleep_for(std::chrono::milliseconds(500));

    std::string data = "{\"test\":\"data\"}";
    Bytes payload(data.begin(), data.end());
    connector_->Send(Packet::FromBytes("TestMessage", std::move(payload)));

    // Process callbacks
    bool completed = WaitForConditionWithMainThreadAction([&]() {
        return error_fired.load();
    }, 5000);

    // Then: OnError should fire with connection closed error
    EXPECT_TRUE(completed) << "OnError event should fire";
    EXPECT_TRUE(error_fired);
    EXPECT_EQ(error_code_received, error_code::CONNECTION_CLOSED);
    EXPECT_FALSE(error_message.empty());
}

TEST_F(A04_OnErrorEventTest, OnError_MultipleHandlers_CanBeUpdated) {
    // Given: Connected to server
    ASSERT_TRUE(CreateStageAndConnect());

    std::atomic<int> handler1_count{0};
    std::atomic<int> handler2_count{0};

    // First handler
    connector_->OnError = [&](int code, std::string message) {
        handler1_count++;
    };

    // Trigger error
    connector_->Disconnect();
    std::this_thread::sleep_for(std::chrono::milliseconds(200));

    std::string data1 = "{\"test\":\"1\"}";
    Bytes payload1(data1.begin(), data1.end());
    connector_->Send(Packet::FromBytes("Test1", std::move(payload1)));

    // Process callbacks
    for (int i = 0; i < 10; i++) {
        connector_->MainThreadAction();
        std::this_thread::sleep_for(std::chrono::milliseconds(10));
    }

    // When: Update handler
    connector_->OnError = [&](int code, std::string message) {
        handler2_count++;
    };

    std::string data2 = "{\"test\":\"2\"}";
    Bytes payload2(data2.begin(), data2.end());
    connector_->Send(Packet::FromBytes("Test2", std::move(payload2)));

    // Process callbacks
    for (int i = 0; i < 10; i++) {
        connector_->MainThreadAction();
        std::this_thread::sleep_for(std::chrono::milliseconds(10));
    }

    // Then: Second handler should fire, not first
    EXPECT_EQ(handler1_count.load(), 1) << "First handler should fire once";
    EXPECT_EQ(handler2_count.load(), 1) << "Second handler should fire once";
}

TEST_F(A04_OnErrorEventTest, OnError_WithoutHandler_DoesNotCrash) {
    // Given: Connected without OnError handler
    ASSERT_TRUE(CreateStageAndConnect());

    // OnError should be cleared for this test
    connector_->OnError = nullptr;
    EXPECT_FALSE(connector_->OnError);

    // When: Trigger error condition
    connector_->Disconnect();
    std::this_thread::sleep_for(std::chrono::milliseconds(200));

    std::string data = "{\"test\":\"data\"}";
    Bytes payload(data.begin(), data.end());
    connector_->Send(Packet::FromBytes("TestMessage", std::move(payload)));

    // Process callbacks
    for (int i = 0; i < 10; i++) {
        connector_->MainThreadAction();
        std::this_thread::sleep_for(std::chrono::milliseconds(10));
    }

    // Then: Should not crash even without handler
    SUCCEED() << "Error handled without OnError handler";
}

TEST_F(A04_OnErrorEventTest, OnError_ErrorCodeTypes_Differentiated) {
    // Given: Connected to server
    ASSERT_TRUE(CreateStageAndConnect());

    std::vector<int> error_codes;

    connector_->OnError = [&](int code, std::string message) {
        error_codes.push_back(code);
    };

    // When: Trigger connection closed error
    connector_->Disconnect();
    std::this_thread::sleep_for(std::chrono::milliseconds(200));

    std::string data = "{\"test\":\"data\"}";
    Bytes payload(data.begin(), data.end());
    connector_->Send(Packet::FromBytes("TestMessage", std::move(payload)));

    // Process callbacks
    for (int i = 0; i < 10; i++) {
        connector_->MainThreadAction();
        std::this_thread::sleep_for(std::chrono::milliseconds(10));
    }

    // Then: Should receive CONNECTION_CLOSED error code
    EXPECT_FALSE(error_codes.empty()) << "Should receive at least one error code";
    if (!error_codes.empty()) {
        EXPECT_EQ(error_codes[0], error_code::CONNECTION_CLOSED);
    }
}

TEST_F(A04_OnErrorEventTest, OnError_MessageProvided_NotEmpty) {
    // Given: Connected to server
    ASSERT_TRUE(CreateStageAndConnect());

    std::atomic<bool> message_not_empty{false};

    connector_->OnError = [&](int code, std::string message) {
        message_not_empty = !message.empty();
    };

    // When: Trigger error
    connector_->Disconnect();
    std::this_thread::sleep_for(std::chrono::milliseconds(200));

    std::string data = "{\"test\":\"data\"}";
    Bytes payload(data.begin(), data.end());
    connector_->Send(Packet::FromBytes("TestMessage", std::move(payload)));

    // Process callbacks
    bool completed = WaitForConditionWithMainThreadAction([&]() {
        return message_not_empty.load();
    }, 5000);

    // Then: Error message should be provided
    EXPECT_TRUE(completed);
    EXPECT_TRUE(message_not_empty) << "Error message should not be empty";
}
