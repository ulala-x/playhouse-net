#include <gtest/gtest.h>
#include <playhouse/extensions/json/json_packet_extensions.hpp>
#include <nlohmann/json.hpp>

using namespace playhouse;
using namespace playhouse::extensions::json;
using json = nlohmann::json;

// Test data structure
struct TestData {
    std::string name;
    int value;
    bool active;

    bool operator==(const TestData& other) const {
        return name == other.name && value == other.value && active == other.active;
    }
};

// JSON serialization for TestData
namespace nlohmann {
    template <>
    struct adl_serializer<TestData> {
        static void to_json(json& j, const TestData& data) {
            j = json{{"name", data.name}, {"value", data.value}, {"active", data.active}};
        }

        static void from_json(const json& j, TestData& data) {
            j.at("name").get_to(data.name);
            j.at("value").get_to(data.value);
            j.at("active").get_to(data.active);
        }
    };
}

class JsonExtensionsTest : public ::testing::Test {
protected:
    void SetUp() override {
        test_data_.name = "TestObject";
        test_data_.value = 42;
        test_data_.active = true;
    }

    TestData test_data_;
};

TEST_F(JsonExtensionsTest, CreateAndParseSimpleObject) {
    // Create packet from object
    auto packet = Create(test_data_, "TestMessage");

    // Verify packet properties
    EXPECT_EQ(packet.GetMsgId(), "TestMessage");
    EXPECT_FALSE(packet.GetPayload().empty());

    // Parse back to object
    auto parsed = Parse<TestData>(packet);

    EXPECT_EQ(parsed, test_data_);
}

TEST_F(JsonExtensionsTest, TryParseValidJson) {
    auto packet = Create(test_data_, "TestMessage");

    auto result = TryParse<TestData>(packet);

    ASSERT_TRUE(result.has_value());
    EXPECT_EQ(result.value(), test_data_);
}

TEST_F(JsonExtensionsTest, TryParseInvalidJson) {
    // Create packet with invalid JSON
    Bytes invalid_payload = {'i', 'n', 'v', 'a', 'l', 'i', 'd'};
    auto packet = Packet::FromBytes("TestMessage", std::move(invalid_payload));

    auto result = TryParse<TestData>(packet);

    EXPECT_FALSE(result.has_value());
}

TEST_F(JsonExtensionsTest, ParseRawJson) {
    auto packet = Create(test_data_, "TestMessage");

    auto json_obj = ParseRaw(packet);

    EXPECT_EQ(json_obj["name"], "TestObject");
    EXPECT_EQ(json_obj["value"], 42);
    EXPECT_EQ(json_obj["active"], true);
}

TEST_F(JsonExtensionsTest, CreateFromJsonObject) {
    json json_obj = {
        {"name", "DirectJson"},
        {"value", 99},
        {"active", false}
    };

    auto packet = CreateFromJson(json_obj, "DirectMessage");

    EXPECT_EQ(packet.GetMsgId(), "DirectMessage");

    auto parsed = Parse<TestData>(packet);
    EXPECT_EQ(parsed.name, "DirectJson");
    EXPECT_EQ(parsed.value, 99);
    EXPECT_FALSE(parsed.active);
}

TEST_F(JsonExtensionsTest, CreateWithOptionsCompact) {
    auto packet = CreateWithOptions(test_data_, "CompactMessage", -1);

    const auto& payload = packet.GetPayload();
    std::string json_str(payload.begin(), payload.end());

    // Compact JSON should not contain newlines
    EXPECT_EQ(json_str.find('\n'), std::string::npos);
}

TEST_F(JsonExtensionsTest, CreateWithOptionsPretty) {
    auto packet = CreateWithOptions(test_data_, "PrettyMessage", 2);

    const auto& payload = packet.GetPayload();
    std::string json_str(payload.begin(), payload.end());

    // Pretty JSON should contain newlines
    EXPECT_NE(json_str.find('\n'), std::string::npos);
}

TEST_F(JsonExtensionsTest, HandleEmptyObject) {
    json empty_obj = json::object();
    auto packet = CreateFromJson(empty_obj, "EmptyMessage");

    auto parsed = ParseRaw(packet);
    EXPECT_TRUE(parsed.is_object());
    EXPECT_TRUE(parsed.empty());
}

TEST_F(JsonExtensionsTest, HandleArray) {
    std::vector<int> numbers = {1, 2, 3, 4, 5};
    auto packet = Create(numbers, "ArrayMessage");

    auto parsed = Parse<std::vector<int>>(packet);
    EXPECT_EQ(parsed, numbers);
}

TEST_F(JsonExtensionsTest, HandleNestedObject) {
    json nested = {
        {"outer", {
            {"inner1", 10},
            {"inner2", "value"}
        }},
        {"array", {1, 2, 3}}
    };

    auto packet = CreateFromJson(nested, "NestedMessage");
    auto parsed = ParseRaw(packet);

    EXPECT_EQ(parsed["outer"]["inner1"], 10);
    EXPECT_EQ(parsed["outer"]["inner2"], "value");
    EXPECT_EQ(parsed["array"].size(), 3);
}

TEST_F(JsonExtensionsTest, HandleUnicodeString) {
    json unicode_obj = {
        {"text", "Hello 世界 🌍"}
    };

    auto packet = CreateFromJson(unicode_obj, "UnicodeMessage");
    auto parsed = ParseRaw(packet);

    EXPECT_EQ(parsed["text"], "Hello 世界 🌍");
}

TEST_F(JsonExtensionsTest, HandleNullValue) {
    json null_obj = {
        {"nullable", nullptr}
    };

    auto packet = CreateFromJson(null_obj, "NullMessage");
    auto parsed = ParseRaw(packet);

    EXPECT_TRUE(parsed["nullable"].is_null());
}

int main(int argc, char **argv) {
    ::testing::InitGoogleTest(&argc, argv);
    return RUN_ALL_TESTS();
}
