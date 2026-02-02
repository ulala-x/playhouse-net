#include <gtest/gtest.h>
#include <playhouse/extensions/msgpack/msgpack_packet_extensions.hpp>
#include <msgpack.hpp>
#include <map>
#include <vector>

using namespace playhouse;
using namespace playhouse::extensions::msgpack;

// Test data structure
struct TestData {
    std::string name;
    int value;
    bool active;

    bool operator==(const TestData& other) const {
        return name == other.name && value == other.value && active == other.active;
    }

    MSGPACK_DEFINE(name, value, active);
};

class MsgPackExtensionsTest : public ::testing::Test {
protected:
    void SetUp() override {
        test_data_.name = "TestObject";
        test_data_.value = 42;
        test_data_.active = true;
    }

    TestData test_data_;
};

TEST_F(MsgPackExtensionsTest, CreateAndParseSimpleObject) {
    // Create packet from object
    auto packet = Create(test_data_, "TestMessage");

    // Verify packet properties
    EXPECT_EQ(packet.GetMsgId(), "TestMessage");
    EXPECT_FALSE(packet.GetPayload().empty());

    // Parse back to object
    auto parsed = Parse<TestData>(packet);

    EXPECT_EQ(parsed, test_data_);
}

TEST_F(MsgPackExtensionsTest, TryParseValidMessagePack) {
    auto packet = Create(test_data_, "TestMessage");

    auto result = TryParse<TestData>(packet);

    ASSERT_TRUE(result.has_value());
    EXPECT_EQ(result.value(), test_data_);
}

TEST_F(MsgPackExtensionsTest, TryParseInvalidMessagePack) {
    // Create packet with invalid MessagePack data
    Bytes invalid_payload = {0xFF, 0xFF, 0xFF, 0xFF};
    auto packet = Packet::FromBytes("TestMessage", std::move(invalid_payload));

    auto result = TryParse<TestData>(packet);

    EXPECT_FALSE(result.has_value());
}

TEST_F(MsgPackExtensionsTest, UnpackRaw) {
    auto packet = Create(test_data_, "TestMessage");

    auto handle = UnpackRaw(packet);
    auto obj = handle.get();

    EXPECT_TRUE(obj.type == ::msgpack::type::ARRAY);
}

TEST_F(MsgPackExtensionsTest, CreateFromBuffer) {
    // Pack data manually
    std::stringstream ss;
    ::msgpack::pack(ss, test_data_);
    std::string buffer = ss.str();

    // Create packet from buffer
    auto packet = CreateFromBuffer(buffer, "BufferMessage");

    EXPECT_EQ(packet.GetMsgId(), "BufferMessage");

    auto parsed = Parse<TestData>(packet);
    EXPECT_EQ(parsed, test_data_);
}

TEST_F(MsgPackExtensionsTest, HandleIntegerTypes) {
    int32_t num = 12345;
    auto packet = Create(num, "IntMessage");

    auto parsed = Parse<int32_t>(packet);
    EXPECT_EQ(parsed, num);
}

TEST_F(MsgPackExtensionsTest, HandleStringTypes) {
    std::string text = "Hello MessagePack";
    auto packet = Create(text, "StringMessage");

    auto parsed = Parse<std::string>(packet);
    EXPECT_EQ(parsed, text);
}

TEST_F(MsgPackExtensionsTest, HandleBooleanTypes) {
    bool flag = true;
    auto packet = Create(flag, "BoolMessage");

    auto parsed = Parse<bool>(packet);
    EXPECT_EQ(parsed, flag);
}

TEST_F(MsgPackExtensionsTest, HandleVectorTypes) {
    std::vector<int> numbers = {1, 2, 3, 4, 5};
    auto packet = Create(numbers, "VectorMessage");

    auto parsed = Parse<std::vector<int>>(packet);
    EXPECT_EQ(parsed, numbers);
}

TEST_F(MsgPackExtensionsTest, HandleMapTypes) {
    std::map<std::string, int> data = {
        {"one", 1},
        {"two", 2},
        {"three", 3}
    };

    auto packet = Create(data, "MapMessage");

    auto parsed = Parse<std::map<std::string, int>>(packet);
    EXPECT_EQ(parsed, data);
}

TEST_F(MsgPackExtensionsTest, CreateArrayWithMultipleTypes) {
    auto packet = CreateArray("ArrayMessage", 42, "hello", true, 3.14);

    EXPECT_EQ(packet.GetMsgId(), "ArrayMessage");
    EXPECT_FALSE(packet.GetPayload().empty());

    // Unpack and verify array structure
    auto handle = UnpackRaw(packet);
    auto obj = handle.get();

    EXPECT_TRUE(obj.type == ::msgpack::type::ARRAY);
    EXPECT_EQ(obj.via.array.size, 4);
}

TEST_F(MsgPackExtensionsTest, HandleEmptyVector) {
    std::vector<int> empty;
    auto packet = Create(empty, "EmptyVectorMessage");

    auto parsed = Parse<std::vector<int>>(packet);
    EXPECT_TRUE(parsed.empty());
}

TEST_F(MsgPackExtensionsTest, HandleEmptyString) {
    std::string empty = "";
    auto packet = Create(empty, "EmptyStringMessage");

    auto parsed = Parse<std::string>(packet);
    EXPECT_TRUE(parsed.empty());
}

TEST_F(MsgPackExtensionsTest, HandleEmptyMap) {
    std::map<std::string, int> empty;
    auto packet = Create(empty, "EmptyMapMessage");

    auto parsed = Parse<std::map<std::string, int>>(packet);
    EXPECT_TRUE(parsed.empty());
}

TEST_F(MsgPackExtensionsTest, HandleNestedStructures) {
    std::map<std::string, std::vector<int>> nested = {
        {"first", {1, 2, 3}},
        {"second", {4, 5, 6}}
    };

    auto packet = Create(nested, "NestedMessage");

    auto parsed = Parse<std::map<std::string, std::vector<int>>>(packet);
    EXPECT_EQ(parsed, nested);
}

TEST_F(MsgPackExtensionsTest, HandleFloatingPoint) {
    double pi = 3.14159265359;
    auto packet = Create(pi, "DoubleMessage");

    auto parsed = Parse<double>(packet);
    EXPECT_DOUBLE_EQ(parsed, pi);
}

TEST_F(MsgPackExtensionsTest, HandleNegativeNumbers) {
    int negative = -12345;
    auto packet = Create(negative, "NegativeMessage");

    auto parsed = Parse<int>(packet);
    EXPECT_EQ(parsed, negative);
}

TEST_F(MsgPackExtensionsTest, HandleUnicodeStrings) {
    std::string unicode = "Hello 世界 🌍";
    auto packet = Create(unicode, "UnicodeMessage");

    auto parsed = Parse<std::string>(packet);
    EXPECT_EQ(parsed, unicode);
}

int main(int argc, char **argv) {
    ::testing::InitGoogleTest(&argc, argv);
    return RUN_ALL_TESTS();
}
