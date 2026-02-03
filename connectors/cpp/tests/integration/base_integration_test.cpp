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
        if (stage_type == "TestStage") {
            stage_info_ = test_server_->GetOrCreateTestStage();
        } else {
            stage_info_ = test_server_->CreateStage(stage_type);
        }
    } catch (const std::exception& e) {
        return false;
    }

    if (!stage_info_.success) {
        return false;
    }

    return ConnectAndWait(5000);
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

bool BaseIntegrationTest::ConnectAndWait(int timeout_ms) {
    if (!connector_) {
        return false;
    }

    bool connected = false;
    bool error = false;

    connector_->OnConnect = [&connected]() { connected = true; };
    connector_->OnError = [&error](int, std::string) { error = true; };

    auto future = connector_->ConnectAsync(
        test_server_->GetHost(),
        test_server_->GetTcpPort()
    );
    (void)future;

    bool finished = WaitForConditionWithMainThreadAction([&]() {
        return connected || error;
    }, timeout_ms);

    return finished && connected;
}

bool BaseIntegrationTest::RequestAndWait(Packet packet, Packet& out_response, int timeout_ms) {
    if (!connector_) {
        return false;
    }

    bool done = false;
    connector_->Request(std::move(packet), [&](Packet response) {
        out_response = std::move(response);
        done = true;
    });

    return WaitForConditionWithMainThreadAction([&]() { return done; }, timeout_ms);
}

bool BaseIntegrationTest::AuthenticateAndWait(Packet packet, bool& out_success, int timeout_ms) {
    if (!connector_) {
        return false;
    }

    bool done = false;
    connector_->Authenticate(std::move(packet), [&](bool success) {
        out_success = success;
        done = true;
    });

    return WaitForConditionWithMainThreadAction([&]() { return done; }, timeout_ms);
}

bool BaseIntegrationTest::AuthenticateTestUser(const std::string& user_id,
                                               const std::string& token,
                                               int timeout_ms) {
    Bytes payload = proto::EncodeAuthenticateRequest(user_id, token);
    Packet auth_packet = Packet::FromBytes("AuthenticateRequest", std::move(payload));
    bool auth_success = false;
    bool completed = AuthenticateAndWait(std::move(auth_packet), auth_success, timeout_ms);
    return completed && auth_success;
}

bool BaseIntegrationTest::CreateStageConnectAndAuthenticate(const std::string& user_id,
                                                            const std::string& token,
                                                            int timeout_ms) {
    if (!CreateStageAndConnect()) {
        return false;
    }
    return AuthenticateTestUser(user_id, token, timeout_ms);
}

TestServerFixture& BaseIntegrationTest::GetTestServer() {
    if (!test_server_) {
        test_server_ = std::make_unique<TestServerFixture>();
    }
    return *test_server_;
}

} // namespace playhouse::test
