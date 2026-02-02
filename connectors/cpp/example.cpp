// Example usage of PlayHouse C++ Connector
// This is a sample file showing basic usage patterns

#include <playhouse/connector.hpp>
#include <iostream>
#include <thread>
#include <chrono>

int main() {
    using namespace playhouse;

    // 1. Configure the connector
    ConnectorConfig config;
    config.heartbeat_interval_ms = 5000;
    config.request_timeout_ms = 10000;
    config.enable_reconnect = false;

    // 2. Create and initialize connector
    Connector connector;
    connector.Init(config);

    // 3. Set up callbacks
    connector.OnConnect = []() {
        std::cout << "Connected to server!" << std::endl;
    };

    connector.OnReceive = [](Packet packet) {
        std::cout << "Received message: " << packet.GetMsgId() << std::endl;
        std::cout << "  MsgSeq: " << packet.GetMsgSeq() << std::endl;
        std::cout << "  StageId: " << packet.GetStageId() << std::endl;
        std::cout << "  ErrorCode: " << packet.GetErrorCode() << std::endl;
        std::cout << "  Payload size: " << packet.GetPayload().size() << " bytes" << std::endl;
    };

    connector.OnError = [](int code, std::string message) {
        std::cerr << "Error " << code << ": " << message << std::endl;
    };

    connector.OnDisconnect = []() {
        std::cout << "Disconnected from server" << std::endl;
    };

    // 4. Connect to server
    std::cout << "Connecting to localhost:34001..." << std::endl;
    auto connect_future = connector.ConnectAsync("localhost", 34001);

    // Wait for connection with timeout
    if (connect_future.wait_for(std::chrono::seconds(5)) == std::future_status::timeout) {
        std::cerr << "Connection timeout" << std::endl;
        return 1;
    }

    bool connected = connect_future.get();
    if (!connected) {
        std::cerr << "Connection failed" << std::endl;
        return 1;
    }

    // Process callbacks
    connector.MainThreadAction();

    // 5. Authenticate (example)
    std::cout << "Authenticating..." << std::endl;
    Bytes auth_payload = {0x01, 0x02, 0x03};  // Replace with actual auth data
    auto auth_future = connector.AuthenticateAsync("game-service", "user123", auth_payload);

    if (auth_future.wait_for(std::chrono::seconds(5)) == std::future_status::timeout) {
        std::cerr << "Authentication timeout" << std::endl;
        connector.Disconnect();
        return 1;
    }

    bool authenticated = auth_future.get();
    if (!authenticated) {
        std::cerr << "Authentication failed" << std::endl;
        connector.Disconnect();
        return 1;
    }

    std::cout << "Authenticated successfully" << std::endl;
    connector.MainThreadAction();

    // 6. Send a message (no response expected)
    std::cout << "Sending notification..." << std::endl;
    Bytes notification_payload = {0xAA, 0xBB, 0xCC, 0xDD};
    Packet notification = Packet::FromBytes("PlayerMove", std::move(notification_payload));
    connector.Send(std::move(notification));

    // 7. Send a request and wait for response
    std::cout << "Sending request..." << std::endl;
    Bytes request_payload = {0x10, 0x20, 0x30};
    Packet request = Packet::FromBytes("GetPlayerInfo", std::move(request_payload));

    auto response_future = connector.RequestAsync(std::move(request));

    if (response_future.wait_for(std::chrono::seconds(10)) == std::future_status::timeout) {
        std::cerr << "Request timeout" << std::endl;
    } else {
        Packet response = response_future.get();
        std::cout << "Received response: " << response.GetMsgId() << std::endl;
        std::cout << "  ErrorCode: " << response.GetErrorCode() << std::endl;
        std::cout << "  Payload size: " << response.GetPayload().size() << " bytes" << std::endl;
    }

    // Process any pending callbacks
    connector.MainThreadAction();

    // 8. Send request with callback
    std::cout << "Sending request with callback..." << std::endl;
    Bytes callback_payload = {0x55, 0x66};
    Packet callback_request = Packet::FromBytes("Echo", std::move(callback_payload));

    connector.Request(std::move(callback_request), [](Packet response) {
        std::cout << "Callback received: " << response.GetMsgId() << std::endl;
        std::cout << "  ErrorCode: " << response.GetErrorCode() << std::endl;
    });

    // Keep processing callbacks for a while
    for (int i = 0; i < 50; ++i) {
        connector.MainThreadAction();
        std::this_thread::sleep_for(std::chrono::milliseconds(100));
    }

    // 9. Disconnect
    std::cout << "Disconnecting..." << std::endl;
    connector.Disconnect();

    // Final callback processing
    connector.MainThreadAction();

    std::cout << "Example completed" << std::endl;
    return 0;
}
