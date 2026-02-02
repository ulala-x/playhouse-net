#include <gtest/gtest.h>
#include <playhouse/connector.hpp>
#include <playhouse/packet.hpp>
#include <playhouse/config.hpp>
#include <thread>
#include <chrono>

using namespace playhouse;

class ConnectorTest : public ::testing::Test {
protected:
    void SetUp() override {
        config_.send_buffer_size = 65536;
        config_.receive_buffer_size = 262144;
        config_.heartbeat_interval_ms = 10000;
        config_.request_timeout_ms = 30000;
    }

    ConnectorConfig config_;
};

TEST_F(ConnectorTest, InitializationTest) {
    Connector connector;

    // Should not throw
    EXPECT_NO_THROW(connector.Init(config_));

    // Should not be connected initially
    EXPECT_FALSE(connector.IsConnected());
}

TEST_F(ConnectorTest, PacketCreation) {
    // Test empty packet
    Packet empty = Packet::Empty("TestMessage");
    EXPECT_EQ(empty.GetMsgId(), "TestMessage");
    EXPECT_TRUE(empty.GetPayload().empty());
    EXPECT_EQ(empty.GetMsgSeq(), 0);
    EXPECT_EQ(empty.GetErrorCode(), 0);

    // Test packet with payload
    Bytes payload = {0x01, 0x02, 0x03, 0x04};
    Packet packet = Packet::FromBytes("DataMessage", std::move(payload));
    EXPECT_EQ(packet.GetMsgId(), "DataMessage");
    EXPECT_EQ(packet.GetPayload().size(), 4);
    EXPECT_EQ(packet.GetPayload()[0], 0x01);
    EXPECT_EQ(packet.GetPayload()[3], 0x04);
}

TEST_F(ConnectorTest, PacketMoveSemantic) {
    Bytes payload = {0xAA, 0xBB, 0xCC};
    Packet packet1("Move Test", std::move(payload));

    // Move constructor
    Packet packet2(std::move(packet1));
    EXPECT_EQ(packet2.GetMsgId(), "Move Test");
    EXPECT_EQ(packet2.GetPayload().size(), 3);
    EXPECT_EQ(packet2.GetPayload()[0], 0xAA);

    // Move assignment
    Packet packet3 = Packet::Empty("Temp");
    packet3 = std::move(packet2);
    EXPECT_EQ(packet3.GetMsgId(), "Move Test");
    EXPECT_EQ(packet3.GetPayload().size(), 3);
}

TEST_F(ConnectorTest, ConfigurationValues) {
    EXPECT_EQ(config_.send_buffer_size, 65536);
    EXPECT_EQ(config_.receive_buffer_size, 262144);
    EXPECT_EQ(config_.heartbeat_interval_ms, 10000);
    EXPECT_EQ(config_.request_timeout_ms, 30000);
    EXPECT_FALSE(config_.enable_reconnect);
}

TEST_F(ConnectorTest, CallbackRegistration) {
    Connector connector;
    connector.Init(config_);

    bool connect_called = false;
    bool disconnect_called = false;
    bool receive_called = false;
    bool error_called = false;

    connector.OnConnect = [&connect_called]() {
        connect_called = true;
    };

    connector.OnDisconnect = [&disconnect_called]() {
        disconnect_called = true;
    };

    connector.OnReceive = [&receive_called](Packet) {
        receive_called = true;
    };

    connector.OnError = [&error_called](int, std::string) {
        error_called = true;
    };

    // Callbacks should be registered (not called yet)
    EXPECT_FALSE(connect_called);
    EXPECT_FALSE(disconnect_called);
    EXPECT_FALSE(receive_called);
    EXPECT_FALSE(error_called);
}

TEST_F(ConnectorTest, ErrorCodeConstants) {
    EXPECT_EQ(error_code::SUCCESS, 0);
    EXPECT_EQ(error_code::CONNECTION_FAILED, 1001);
    EXPECT_EQ(error_code::CONNECTION_TIMEOUT, 1002);
    EXPECT_EQ(error_code::CONNECTION_CLOSED, 1003);
    EXPECT_EQ(error_code::REQUEST_TIMEOUT, 2001);
    EXPECT_EQ(error_code::INVALID_RESPONSE, 2002);
    EXPECT_EQ(error_code::AUTHENTICATION_FAILED, 3001);
}

TEST_F(ConnectorTest, SpecialMessageIds) {
    EXPECT_STREQ(msg_id::HEARTBEAT, "@Heart@Beat@");
    EXPECT_STREQ(msg_id::DEBUG, "@Debug@");
    EXPECT_STREQ(msg_id::TIMEOUT, "@Timeout@");
}

TEST_F(ConnectorTest, ProtocolConstants) {
    EXPECT_EQ(protocol::MAX_MSG_ID_LENGTH, 256);
    EXPECT_EQ(protocol::MAX_BODY_SIZE, 1024 * 1024 * 2);
    EXPECT_EQ(protocol::MIN_HEADER_SIZE, 21);
    EXPECT_EQ(protocol::REQUEST_HEADER_SIZE, 15);
}

TEST_F(ConnectorTest, CallbackOverloadTest) {
    Connector connector;
    connector.Init(config_);

    bool callback_invoked = false;
    Packet receivedPacket = Packet::Empty("Empty");

    // Test Request() callback overload
    auto callback = [&callback_invoked, &receivedPacket](Packet packet) {
        callback_invoked = true;
        receivedPacket = std::move(packet);
    };

    // Should compile and accept the callback
    EXPECT_NO_THROW({
        Packet testPacket = Packet::Empty("CallbackTest");
        connector.Request(std::move(testPacket), callback);
    });

    // Callback should not be called yet (no server connection)
    EXPECT_FALSE(callback_invoked);
}

TEST_F(ConnectorTest, AuthenticateCallbackOverloadTest) {
    Connector connector;
    connector.Init(config_);

    bool auth_callback_invoked = false;
    bool auth_result = false;

    // Test Authenticate() callback overload
    auto callback = [&auth_callback_invoked, &auth_result](bool success) {
        auth_callback_invoked = true;
        auth_result = success;
    };

    // Should compile and accept the callback
    EXPECT_NO_THROW({
        Packet authPacket = Packet::Empty("AuthTest");
        connector.Authenticate(std::move(authPacket), callback);
    });

    // Callback should not be called yet (no server connection)
    EXPECT_FALSE(auth_callback_invoked);
}

int main(int argc, char **argv) {
    ::testing::InitGoogleTest(&argc, argv);
    return RUN_ALL_TESTS();
}
