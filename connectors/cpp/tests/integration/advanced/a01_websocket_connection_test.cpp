#include "../base_integration_test.hpp"
#include <gtest/gtest.h>
#include <atomic>

using namespace playhouse;
using namespace playhouse::test;

/// A-01: WebSocket Connection Tests
/// Verifies WebSocket transport layer functionality
class A01_WebSocketConnectionTest : public BaseIntegrationTest {
protected:
    void ConfigureWebSocket() {
        config_.use_websocket = true;
        config_.websocket_path = "/ws";
        connector_->Init(config_);
    }

    bool ConnectWebSocketAndWait(int timeout_ms = 5000) {
        bool connected = false;
        bool error = false;

        connector_->OnConnect = [&connected]() { connected = true; };
        connector_->OnError = [&error](int, std::string) { error = true; };

        auto future = connector_->ConnectAsync(
            GetTestServer().GetHost(),
            GetTestServer().GetWsPort()
        );
        (void)future;

        bool finished = WaitForConditionWithMainThreadAction([&]() {
            return connected || error;
        }, timeout_ms);

        return finished && connected;
    }
};

TEST_F(A01_WebSocketConnectionTest, WebSocket_Connect_Succeeds) {
    ASSERT_TRUE(GetTestServer().GetOrCreateTestStage().success);
    ConfigureWebSocket();
    EXPECT_TRUE(ConnectWebSocketAndWait());
}

TEST_F(A01_WebSocketConnectionTest, WebSocket_Authenticate_Succeeds) {
    ASSERT_TRUE(GetTestServer().GetOrCreateTestStage().success);
    ConfigureWebSocket();
    ASSERT_TRUE(ConnectWebSocketAndWait());
    EXPECT_TRUE(AuthenticateTestUser("ws_auth_user"));
}

TEST_F(A01_WebSocketConnectionTest, WebSocket_Echo_RequestResponse) {
    ASSERT_TRUE(GetTestServer().GetOrCreateTestStage().success);
    ConfigureWebSocket();
    ASSERT_TRUE(ConnectWebSocketAndWait());
    ASSERT_TRUE(AuthenticateTestUser("ws_echo_user"));

    Bytes payload = proto::EncodeEchoRequest("ws-echo", 1);
    Packet request = Packet::FromBytes("EchoRequest", std::move(payload));
    Packet response = Packet::Empty("Empty");

    ASSERT_TRUE(RequestAndWait(std::move(request), response, 5000));

    std::string text;
    int32_t number = 0;
    ASSERT_TRUE(proto::DecodeEchoReply(response.GetPayload(), text, number));
    EXPECT_EQ(text, "ws-echo");
    EXPECT_EQ(number, 1);
}

TEST_F(A01_WebSocketConnectionTest, WebSocket_PushMessage_Received) {
    ASSERT_TRUE(GetTestServer().GetOrCreateTestStage().success);
    ConfigureWebSocket();
    ASSERT_TRUE(ConnectWebSocketAndWait());
    ASSERT_TRUE(AuthenticateTestUser("ws_push_user"));

    bool received = false;
    connector_->OnReceive = [&](Packet packet) {
        if (packet.GetMsgId() == "BroadcastNotify") {
            std::string event_type;
            std::string payload;
            if (proto::DecodeBroadcastNotify(packet.GetPayload(), event_type, payload)) {
                received = (payload == "ws-broadcast");
            }
        }
    };

    Bytes payload = proto::EncodeBroadcastRequest("ws-broadcast");
    connector_->Send(Packet::FromBytes("BroadcastRequest", std::move(payload)));

    EXPECT_TRUE(WaitForConditionWithMainThreadAction([&]() { return received; }, 5000));
}

TEST_F(A01_WebSocketConnectionTest, WebSocket_Reconnect_Succeeds) {
    ASSERT_TRUE(GetTestServer().GetOrCreateTestStage().success);
    ConfigureWebSocket();
    ASSERT_TRUE(ConnectWebSocketAndWait());
    connector_->Disconnect();

    EXPECT_TRUE(ConnectWebSocketAndWait());
}

TEST_F(A01_WebSocketConnectionTest, WebSocket_ParallelRequests_Handled) {
    ASSERT_TRUE(GetTestServer().GetOrCreateTestStage().success);
    ConfigureWebSocket();
    ASSERT_TRUE(ConnectWebSocketAndWait());
    ASSERT_TRUE(AuthenticateTestUser("ws_parallel_user"));

    std::atomic<int> completed{0};
    for (int i = 0; i < 5; i++) {
        Bytes payload = proto::EncodeEchoRequest("ws-parallel", i);
        Packet request = Packet::FromBytes("EchoRequest", std::move(payload));
        connector_->Request(std::move(request), [&](Packet response) {
            std::string text;
            int32_t number = 0;
            if (proto::DecodeEchoReply(response.GetPayload(), text, number) && text == "ws-parallel") {
                completed.fetch_add(1);
            }
        });
    }

    EXPECT_TRUE(WaitForConditionWithMainThreadAction([&]() {
        return completed.load() == 5;
    }, 5000));
}
