#include "base_integration_test.hpp"
#include <thread>
#include <chrono>

namespace playhouse::test {

std::unique_ptr<TestServerFixture> BaseIntegrationTest::test_server_;

void BaseIntegrationTest::SetUp() {
    // Initialize test server fixture (singleton pattern)
    if (!test_server_) {
        test_server_ = std::make_unique<TestServerFixture>();
    }

    // Create connector with default config
    connector_ = std::make_unique<Connector>();

    config_.send_buffer_size = 65536;
    config_.receive_buffer_size = 262144;
    config_.heartbeat_interval_ms = 10000;
    config_.request_timeout_ms = 5000;  // Shorter timeout for tests
    config_.enable_reconnect = false;

    connector_->Init(config_);
}

void BaseIntegrationTest::TearDown() {
    if (connector_) {
        if (connector_->IsConnected()) {
            connector_->Disconnect();
            // Wait a bit for clean disconnect
            std::this_thread::sleep_for(std::chrono::milliseconds(100));
        }
        connector_.reset();
    }
}

bool BaseIntegrationTest::CreateStageAndConnect(const std::string& stage_type) {
    // Create stage via HTTP API
    try {
        stage_info_ = test_server_->CreateStage(stage_type);
    } catch (const std::exception& e) {
        return false;
    }

    if (!stage_info_.success) {
        return false;
    }

    // Connect to the stage
    auto future = connector_->ConnectAsync(
        test_server_->GetHost(),
        test_server_->GetTcpPort(),
        stage_info_.stage_id,
        stage_info_.stage_type
    );

    try {
        return WaitWithMainThreadAction(future, 5000);
    } catch (...) {
        return false;
    }
}

bool BaseIntegrationTest::WaitForConditionWithMainThreadAction(
    std::function<bool()> condition,
    int timeout_ms
) {
    auto deadline = std::chrono::steady_clock::now() + std::chrono::milliseconds(timeout_ms);

    while (!condition()) {
        if (std::chrono::steady_clock::now() >= deadline) {
            return false;
        }

        if (connector_) {
            connector_->MainThreadAction();
        }

        std::this_thread::sleep_for(std::chrono::milliseconds(10));
    }

    return true;
}

TestServerFixture& BaseIntegrationTest::GetTestServer() {
    if (!test_server_) {
        test_server_ = std::make_unique<TestServerFixture>();
    }
    return *test_server_;
}

} // namespace playhouse::test
