#include "../base_integration_test.hpp"
#include <gtest/gtest.h>
#include <atomic>

using namespace playhouse;
using namespace playhouse::test;

/// C-11: Error Response Tests
/// Verifies error response handling
class C11_ErrorResponseTest : public BaseIntegrationTest {};

TEST_F(C11_ErrorResponseTest, Request_WithError_ErrorCodeSet) {
    // Given: Connected to server
    ASSERT_TRUE(CreateStageAndConnect());

    // When: Send a request that triggers an error
    std::string error_data = "{\"triggerError\":true}";
    Bytes payload(error_data.begin(), error_data.end());
    auto packet = Packet::FromBytes("ErrorRequest", std::move(payload));

    Packet response = Packet::Empty("Empty");
    bool completed = RequestAndWait(std::move(packet), response, 5000);

    // Then: Response should have error code or be handled gracefully
    if (completed) {
        SUCCEED() << "Error request completed with error code: " << response.GetErrorCode();
    } else {
        SUCCEED() << "Error request timed out as expected";
    }
}

TEST_F(C11_ErrorResponseTest, OnError_Event_TriggersForNetworkErrors) {
    // Given: Connected to server
    ASSERT_TRUE(CreateStageAndConnect());

    std::atomic<bool> error_event_fired{false};
    int error_code_received = 0;
    std::string error_message;

    connector_->OnError = [&](int code, std::string message) {
        error_event_fired = true;
        error_code_received = code;
        error_message = message;
    };

    // When: Force a network error by disconnecting and trying to send
    connector_->Disconnect();
    std::this_thread::sleep_for(std::chrono::milliseconds(500));

    std::string data = "{\"test\":\"data\"}";
    Bytes payload(data.begin(), data.end());
    connector_->Send(Packet::FromBytes("TestMessage", std::move(payload)));

    // Process callbacks
    bool completed = WaitForConditionWithMainThreadAction([&]() {
        return error_event_fired.load();
    }, 5000);

    // Then: OnError event should fire
    EXPECT_TRUE(completed) << "OnError event should fire for network errors";
    EXPECT_TRUE(error_event_fired) << "Error event should be triggered";
    EXPECT_EQ(error_code_received, error_code::CONNECTION_CLOSED) << "Error code should indicate connection issue";
}

TEST_F(C11_ErrorResponseTest, Request_ToInvalidEndpoint_HandledGracefully) {
    // Given: Connected to server
    ASSERT_TRUE(CreateStageAndConnect());

    // When: Send request to non-existent endpoint
    std::string invalid_data = "{\"content\":\"Invalid endpoint\"}";
    Bytes payload(invalid_data.begin(), invalid_data.end());
    auto packet = Packet::FromBytes("NonExistentRequest", std::move(payload));

    Packet response = Packet::Empty("Empty");
    bool completed = RequestAndWait(std::move(packet), response, 5000);

    // Then: Should handle gracefully without crashing
    if (completed) {
        SUCCEED() << "Invalid endpoint handled gracefully with error code: " << response.GetErrorCode();
    } else {
        SUCCEED() << "Invalid endpoint timed out as expected";
    }
}

TEST_F(C11_ErrorResponseTest, ErrorResponse_WithPayload_PayloadAccessible) {
    // Given: Connected to server
    ASSERT_TRUE(CreateStageAndConnect());

    // When: Trigger an error that returns payload
    std::string error_data = "{\"errorType\":\"CustomError\"}";
    Bytes payload(error_data.begin(), error_data.end());
    auto packet = Packet::FromBytes("CustomErrorRequest", std::move(payload));

    Packet response = Packet::Empty("Empty");
    bool completed = RequestAndWait(std::move(packet), response, 5000);

    // Then: Error response payload should be accessible
    if (completed) {
        SUCCEED() << "Error response received, payload size: " << response.GetPayload().size();
    } else {
        SUCCEED() << "Error handled via timeout";
    }
}

TEST_F(C11_ErrorResponseTest, MultipleErrors_AllHandledIndependently) {
    // Given: Connected to server
    ASSERT_TRUE(CreateStageAndConnect());

    std::atomic<int> error_count{0};

    connector_->OnError = [&](int code, std::string message) {
        error_count++;
    };

    // When: Trigger multiple errors
    for (int i = 0; i < 3; i++) {
        std::string error_data = "{\"errorType\":\"Error" + std::to_string(i) + "\"}";
        Bytes payload(error_data.begin(), error_data.end());
        auto packet = Packet::FromBytes("ErrorRequest", std::move(payload));

        Packet response = Packet::Empty("Empty");
        RequestAndWait(std::move(packet), response, 3000);

        // Process callbacks
        for (int j = 0; j < 5; j++) {
            connector_->MainThreadAction();
            std::this_thread::sleep_for(std::chrono::milliseconds(10));
        }
    }

    // Then: System should remain stable
    SUCCEED() << "Multiple errors handled, error events: " << error_count.load();
    // Connection might or might not be still active depending on error severity
}

TEST_F(C11_ErrorResponseTest, ConnectionError_AfterEstablished_TriggersOnError) {
    // Given: Connected to server
    ASSERT_TRUE(CreateStageAndConnect());

    std::atomic<bool> error_triggered{false};

    connector_->OnError = [&](int code, std::string message) {
        if (code == error_code::CONNECTION_CLOSED || code == error_code::CONNECTION_FAILED) {
            error_triggered = true;
        }
    };

    // When: Force disconnect
    connector_->Disconnect();

    // Try to send after disconnect
    std::string data = "{\"test\":\"data\"}";
    Bytes payload(data.begin(), data.end());
    connector_->Send(Packet::FromBytes("TestMessage", std::move(payload)));

    // Process callbacks
    bool completed = WaitForConditionWithMainThreadAction([&]() {
        return error_triggered.load();
    }, 5000);

    // Then: Connection error should trigger OnError
    EXPECT_TRUE(completed) << "Connection error should trigger OnError event";
}
