#ifndef PLAYHOUSE_BASE_INTEGRATION_TEST_HPP
#define PLAYHOUSE_BASE_INTEGRATION_TEST_HPP

#include <gtest/gtest.h>
#include <playhouse/connector.hpp>
#include <playhouse/packet.hpp>
#include <playhouse/config.hpp>
#include "test_server_fixture.hpp"
#include <memory>
#include <functional>
#include <chrono>

namespace playhouse::test {

/// Base class for integration tests
/// Provides common setup, teardown, and helper methods for testing the Connector
class BaseIntegrationTest : public ::testing::Test {
protected:
    /// Setup before each test
    void SetUp() override;

    /// Teardown after each test
    void TearDown() override;

    /// Create a stage and connect to it (helper method)
    /// @param stage_type Stage type to create (default: "TestStage")
    /// @return true if connection succeeded
    bool CreateStageAndConnect(const std::string& stage_type = "TestStage");

    /// Wait for a condition while calling MainThreadAction
    /// Useful for callback-based APIs that require MainThreadAction to be called
    /// @param condition Function that returns true when condition is met
    /// @param timeout_ms Timeout in milliseconds
    /// @return true if condition was met before timeout
    bool WaitForConditionWithMainThreadAction(
        std::function<bool()> condition,
        int timeout_ms = 5000
    );

    /// Connect to server and wait for OnConnect callback
    bool ConnectAndWait(int timeout_ms = 5000);

    /// Send request and wait for response callback
    bool RequestAndWait(Packet packet, Packet& out_response, int timeout_ms = 5000);

    /// Authenticate and wait for callback
    bool AuthenticateAndWait(Packet packet, bool& out_success, int timeout_ms = 5000);

    /// Get the test server fixture (singleton)
    static TestServerFixture& GetTestServer();

    // Test resources
    std::unique_ptr<Connector> connector_;
    CreateStageResponse stage_info_;
    ConnectorConfig config_;

private:
    static std::unique_ptr<TestServerFixture> test_server_;
};

} // namespace playhouse::test

#endif // PLAYHOUSE_BASE_INTEGRATION_TEST_HPP
