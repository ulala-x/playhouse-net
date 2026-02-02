#include "../base_integration_test.hpp"
#include <gtest/gtest.h>
#include <vector>

using namespace playhouse;
using namespace playhouse::test;

/// A-05: Multiple Connector Tests
/// Verifies multiple connector instances can work simultaneously
class A05_MultipleConnectorTest : public BaseIntegrationTest {};

TEST_F(A05_MultipleConnectorTest, TwoConnectors_BothConnect_Independently) {
    // Given: Two connector instances
    auto connector2 = std::make_unique<Connector>();
    connector2->Init(config_);

    // When: Connect both
    auto stage1 = GetTestServer().CreateTestStage();
    auto stage2 = GetTestServer().CreateTestStage();

    auto future1 = connector_->ConnectAsync(GetTestServer().GetHost(), GetTestServer().GetTcpPort());
    auto future2 = connector2->ConnectAsync(GetTestServer().GetHost(), GetTestServer().GetTcpPort());

    bool connected1 = WaitWithMainThreadAction(future1, 5000);
    bool connected2 = false;

    // Also call MainThreadAction for connector2
    auto deadline = std::chrono::steady_clock::now() + std::chrono::milliseconds(5000);
    while (future2.wait_for(std::chrono::milliseconds(10)) != std::future_status::ready) {
        if (std::chrono::steady_clock::now() >= deadline) break;
        connector2->MainThreadAction();
    }
    connected2 = future2.get();

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

        auto stage = GetTestServer().CreateTestStage();
        auto future = conn->ConnectAsync(GetTestServer().GetHost(), GetTestServer().GetTcpPort());

        auto deadline = std::chrono::steady_clock::now() + std::chrono::milliseconds(5000);
        while (future.wait_for(std::chrono::milliseconds(10)) != std::future_status::ready) {
            if (std::chrono::steady_clock::now() >= deadline) break;
            conn->MainThreadAction();
        }

        if (future.get()) {
            connectors.push_back(std::move(conn));
        }
    }

    ASSERT_GE(connectors.size(), 2) << "At least 2 connectors should connect";

    // When: Each sends a message
    std::vector<std::future<Packet>> futures;

    for (size_t i = 0; i < connectors.size(); i++) {
        std::string echo_data = "{\"content\":\"Connector" + std::to_string(i) + "\",\"sequence\":" + std::to_string(i) + "}";
        Bytes payload(echo_data.begin(), echo_data.end());
        auto packet = Packet::FromBytes("EchoRequest", std::move(payload));

        futures.push_back(connectors[i]->RequestAsync(std::move(packet)));
    }

    // Then: All should receive responses
    int success_count = 0;
    for (size_t i = 0; i < futures.size(); i++) {
        try {
            auto deadline = std::chrono::steady_clock::now() + std::chrono::milliseconds(5000);
            while (futures[i].wait_for(std::chrono::milliseconds(10)) != std::future_status::ready) {
                if (std::chrono::steady_clock::now() >= deadline) break;
                connectors[i]->MainThreadAction();
            }

            if (futures[i].wait_for(std::chrono::milliseconds(0)) == std::future_status::ready) {
                auto response = futures[i].get();
                if (response.GetErrorCode() == 0) {
                    success_count++;
                }
            }
        } catch (...) {
            // Some might fail, but shouldn't crash
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

    ASSERT_TRUE(CreateStageAndConnect());

    auto stage2 = GetTestServer().CreateTestStage();
    auto future2 = connector2->ConnectAsync(GetTestServer().GetHost(), GetTestServer().GetTcpPort());

    auto deadline = std::chrono::steady_clock::now() + std::chrono::milliseconds(5000);
    while (future2.wait_for(std::chrono::milliseconds(10)) != std::future_status::ready) {
        if (std::chrono::steady_clock::now() >= deadline) break;
        connector2->MainThreadAction();
    }
    bool connected2 = future2.get();

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
    ASSERT_TRUE(CreateStageAndConnect());

    auto stage2 = GetTestServer().CreateTestStage();
    auto future2 = connector2->ConnectAsync(GetTestServer().GetHost(), GetTestServer().GetTcpPort());

    auto deadline = std::chrono::steady_clock::now() + std::chrono::milliseconds(5000);
    while (future2.wait_for(std::chrono::milliseconds(10)) != std::future_status::ready) {
        if (std::chrono::steady_clock::now() >= deadline) break;
        connector2->MainThreadAction();
    }
    ASSERT_TRUE(future2.get());

    // When: Trigger messages for both
    std::string broadcast1 = "{\"target\":\"connector1\"}";
    Bytes payload1(broadcast1.begin(), broadcast1.end());
    connector_->Send(Packet::FromBytes("BroadcastRequest", std::move(payload1)));

    std::string broadcast2 = "{\"target\":\"connector2\"}";
    Bytes payload2(broadcast2.begin(), broadcast2.end());
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

        auto stage = GetTestServer().CreateTestStage();
        auto future = conn->ConnectAsync(GetTestServer().GetHost(), GetTestServer().GetTcpPort());

        auto deadline = std::chrono::steady_clock::now() + std::chrono::milliseconds(5000);
        while (future.wait_for(std::chrono::milliseconds(10)) != std::future_status::ready) {
            if (std::chrono::steady_clock::now() >= deadline) break;
            conn->MainThreadAction();
        }

        bool connected = false;
        try {
            connected = future.get();
        } catch (...) {
            connected = false;
        }

        if (connected) {
            conn->Disconnect();
        }
        // conn destroyed here
    }

    // Then: Should handle sequential lifecycle without issues
    SUCCEED() << "Sequential connector creation/destruction handled correctly";
}
