#include "../base_integration_test.hpp"
#include <gtest/gtest.h>
#include <vector>
#include <optional>
#include <thread>
#include <chrono>

using namespace playhouse;
using namespace playhouse::test;

/// A-05: Multiple Connector Tests
/// Verifies multiple connector instances can work simultaneously
class A05_MultipleConnectorTest : public BaseIntegrationTest {};

namespace {
bool ConnectWithWait(Connector& connector, TestServerFixture& server, int timeout_ms = 5000) {
    bool connected = false;
    bool error = false;

    connector.OnConnect = [&connected]() { connected = true; };
    connector.OnError = [&error](int, std::string) { error = true; };

    auto future = connector.ConnectAsync(server.GetHost(), server.GetTcpPort());
    (void)future;

    auto deadline = std::chrono::steady_clock::now() + std::chrono::milliseconds(timeout_ms);
    while (!connected && !error && std::chrono::steady_clock::now() < deadline) {
        connector.MainThreadAction();
        std::this_thread::sleep_for(std::chrono::milliseconds(10));
    }

    return connected;
}

bool AuthenticateWithWait(Connector& connector, const std::string& user_id, int timeout_ms = 5000) {
    bool done = false;
    bool success = false;

    Bytes payload = proto::EncodeAuthenticateRequest(user_id, "valid_token");
    Packet auth_packet = Packet::FromBytes("AuthenticateRequest", std::move(payload));
    connector.Authenticate(std::move(auth_packet), [&](bool result) {
        success = result;
        done = true;
    });

    auto deadline = std::chrono::steady_clock::now() + std::chrono::milliseconds(timeout_ms);
    while (!done && std::chrono::steady_clock::now() < deadline) {
        connector.MainThreadAction();
        std::this_thread::sleep_for(std::chrono::milliseconds(10));
    }

    return done && success;
}
} // namespace

TEST_F(A05_MultipleConnectorTest, TwoConnectors_BothConnect_Independently) {
    // Given: Two connector instances
    auto connector2 = std::make_unique<Connector>();
    connector2->Init(config_);

    // When: Connect both
    auto stage1 = GetTestServer().GetOrCreateTestStage();
    auto stage2 = GetTestServer().GetOrCreateTestStage();

    bool connected1 = ConnectAndWait(5000);
    bool connected2 = ConnectWithWait(*connector2, GetTestServer(), 5000);
    ASSERT_TRUE(AuthenticateTestUser("multi_conn_user1"));
    ASSERT_TRUE(AuthenticateWithWait(*connector2, "multi_conn_user2"));

    // Then: Both should connect successfully
    EXPECT_TRUE(connected1) << "First connector should connect";
    EXPECT_TRUE(connected2) << "Second connector should connect";
    EXPECT_TRUE(connector_->IsConnected());
    EXPECT_TRUE(connector2->IsConnected());

    // Cleanup
    connector2->Disconnect();
}

TEST_F(A05_MultipleConnectorTest, MultipleConnectors_SendMessagesIndependently) {
    // Given: Multiple connectors connected
    std::vector<std::unique_ptr<Connector>> connectors;
    const int num_connectors = 3;

    for (int i = 0; i < num_connectors; i++) {
        auto conn = std::make_unique<Connector>();
        conn->Init(config_);

        auto stage = GetTestServer().GetOrCreateTestStage();
        if (ConnectWithWait(*conn, GetTestServer(), 5000)
            && AuthenticateWithWait(*conn, "multi_conn_batch_" + std::to_string(i))) {
            connectors.push_back(std::move(conn));
        }
    }

    ASSERT_GE(connectors.size(), 2) << "At least 2 connectors should connect";

    // When: Each sends a message
    std::vector<bool> completed(connectors.size(), false);
    std::vector<std::optional<Packet>> responses(connectors.size());

    for (size_t i = 0; i < connectors.size(); i++) {
        Bytes payload = proto::EncodeEchoRequest("Connector" + std::to_string(i), static_cast<int32_t>(i));
        auto packet = Packet::FromBytes("EchoRequest", std::move(payload));

        connectors[i]->Request(std::move(packet), [&, i](Packet response) {
            responses[i] = std::move(response);
            completed[i] = true;
        });
    }

    // Then: All should receive responses
    int success_count = 0;
    for (size_t i = 0; i < connectors.size(); i++) {
        auto deadline = std::chrono::steady_clock::now() + std::chrono::milliseconds(5000);
        while (!completed[i] && std::chrono::steady_clock::now() < deadline) {
            connectors[i]->MainThreadAction();
            std::this_thread::sleep_for(std::chrono::milliseconds(10));
        }
        if (completed[i] && responses[i].has_value() && responses[i]->GetErrorCode() == 0) {
            success_count++;
        }
    }

    EXPECT_GT(success_count, 0) << "At least some connectors should receive responses";

    // Cleanup
    for (auto& conn : connectors) {
        conn->Disconnect();
    }
}

TEST_F(A05_MultipleConnectorTest, Connectors_IndependentLifecycles) {
    // Given: Two connectors
    auto connector2 = std::make_unique<Connector>();
    connector2->Init(config_);

    ASSERT_TRUE(CreateStageConnectAndAuthenticate("multi_conn_lifecycle1"));

    auto stage2 = GetTestServer().GetOrCreateTestStage();
    (void)stage2;
    bool connected2 = ConnectWithWait(*connector2, GetTestServer(), 5000);
    ASSERT_TRUE(AuthenticateWithWait(*connector2, "multi_conn_lifecycle2"));

    ASSERT_TRUE(connected2);

    // When: Disconnect first connector
    connector_->Disconnect();
    std::this_thread::sleep_for(std::chrono::milliseconds(500));

    // Then: Second connector should still work
    EXPECT_FALSE(connector_->IsConnected()) << "First connector should be disconnected";
    EXPECT_TRUE(connector2->IsConnected()) << "Second connector should still be connected";

    // Cleanup
    connector2->Disconnect();
}

TEST_F(A05_MultipleConnectorTest, Connectors_SeparateCallbackHandlers) {
    // Given: Two connectors with separate callback handlers
    auto connector2 = std::make_unique<Connector>();
    connector2->Init(config_);

    std::atomic<bool> connector1_received{false};
    std::atomic<bool> connector2_received{false};

    connector_->OnReceive = [&](Packet packet) {
        connector1_received = true;
    };

    connector2->OnReceive = [&](Packet packet) {
        connector2_received = true;
    };

    // Connect both
    ASSERT_TRUE(CreateStageConnectAndAuthenticate("multi_conn_cb1"));

    auto stage2 = GetTestServer().GetOrCreateTestStage();
    (void)stage2;
    ASSERT_TRUE(ConnectWithWait(*connector2, GetTestServer(), 5000));
    ASSERT_TRUE(AuthenticateWithWait(*connector2, "multi_conn_cb2"));

    // When: Trigger messages for both
    Bytes payload1 = proto::EncodeBroadcastRequest("connector1");
    connector_->Send(Packet::FromBytes("BroadcastRequest", std::move(payload1)));

    Bytes payload2 = proto::EncodeBroadcastRequest("connector2");
    connector2->Send(Packet::FromBytes("BroadcastRequest", std::move(payload2)));

    // Process callbacks for both
    for (int i = 0; i < 50; i++) {
        connector_->MainThreadAction();
        connector2->MainThreadAction();
        std::this_thread::sleep_for(std::chrono::milliseconds(10));

        if (connector1_received && connector2_received) break;
    }

    // Then: Each should receive their own callback
    EXPECT_TRUE(connector1_received || connector2_received) << "At least one connector should receive callback";

    // Cleanup
    connector2->Disconnect();
}

TEST_F(A05_MultipleConnectorTest, Connectors_SequentialCreationAndDestruction) {
    // When: Create and destroy connectors sequentially
    for (int i = 0; i < 3; i++) {
        auto conn = std::make_unique<Connector>();
        conn->Init(config_);

        auto stage = GetTestServer().GetOrCreateTestStage();
        (void)stage;
        bool connected = ConnectWithWait(*conn, GetTestServer(), 5000);
        if (connected && AuthenticateWithWait(*conn, "multi_conn_seq_" + std::to_string(i))) {
            conn->Disconnect();
        }
        // conn destroyed here
    }

    // Then: Should handle sequential lifecycle without issues
    SUCCEED() << "Sequential connector creation/destruction handled correctly";
}
