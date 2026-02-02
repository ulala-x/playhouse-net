#include <gtest/gtest.h>
#include <playhouse/extensions/proto/proto_packet_extensions.hpp>

// For testing purposes, we'll create simple inline proto messages
// In a real scenario, these would be generated from .proto files

// Simple test message using protobuf lite
namespace test_proto {

// Simulated protobuf message for testing
class SimpleMessage : public google::protobuf::MessageLite {
public:
    SimpleMessage() : id_(0), value_(0) {}

    void set_id(int32_t id) { id_ = id; }
    int32_t id() const { return id_; }

    void set_name(const std::string& name) { name_ = name; }
    const std::string& name() const { return name_; }

    void set_value(double value) { value_ = value; }
    double value() const { return value_; }

    // MessageLite interface implementation
    std::string GetTypeName() const override { return "SimpleMessage"; }

    google::protobuf::MessageLite* New(google::protobuf::Arena* arena) const override {
        return new SimpleMessage();
    }

    void Clear() override {
        id_ = 0;
        name_.clear();
        value_ = 0.0;
    }

    bool IsInitialized() const override { return true; }

    size_t ByteSizeLong() const override {
        return sizeof(id_) + name_.size() + sizeof(value_) + 10; // rough estimate
    }

    bool SerializeToString(std::string* output) const {
        output->clear();
        // Simple serialization (not real protobuf format, just for testing)
        output->append(reinterpret_cast<const char*>(&id_), sizeof(id_));
        uint32_t name_len = static_cast<uint32_t>(name_.size());
        output->append(reinterpret_cast<const char*>(&name_len), sizeof(name_len));
        output->append(name_);
        output->append(reinterpret_cast<const char*>(&value_), sizeof(value_));
        return true;
    }

    bool ParseFromArray(const void* data, int size) {
        if (size < static_cast<int>(sizeof(id_) + sizeof(uint32_t))) {
            return false;
        }

        const char* ptr = static_cast<const char*>(data);

        std::memcpy(&id_, ptr, sizeof(id_));
        ptr += sizeof(id_);

        uint32_t name_len;
        std::memcpy(&name_len, ptr, sizeof(name_len));
        ptr += sizeof(name_len);

        if (size < static_cast<int>(sizeof(id_) + sizeof(uint32_t) + name_len + sizeof(value_))) {
            return false;
        }

        name_.assign(ptr, name_len);
        ptr += name_len;

        std::memcpy(&value_, ptr, sizeof(value_));

        return true;
    }

    int GetCachedSize() const override { return static_cast<int>(ByteSizeLong()); }

    const char* _InternalParse(const char* ptr,
                               google::protobuf::internal::ParseContext* ctx) override {
        return ptr;
    }

    uint8_t* _InternalSerialize(
        uint8_t* target,
        google::protobuf::io::EpsCopyOutputStream* stream) const override {
        return target;
    }

private:
    int32_t id_;
    std::string name_;
    double value_;
};

} // namespace test_proto

using namespace playhouse;
using namespace playhouse::extensions::proto;

class ProtoExtensionsTest : public ::testing::Test {
protected:
    void SetUp() override {
        test_message_.set_id(42);
        test_message_.set_name("TestMessage");
        test_message_.set_value(3.14159);
    }

    test_proto::SimpleMessage test_message_;
};

TEST_F(ProtoExtensionsTest, CreateAndParseSimpleMessage) {
    // Create packet from message
    auto packet = Create(test_message_, "ProtoMessage");

    // Verify packet properties
    EXPECT_EQ(packet.GetMsgId(), "ProtoMessage");
    EXPECT_FALSE(packet.GetPayload().empty());

    // Parse back to message
    auto parsed = Parse<test_proto::SimpleMessage>(packet);

    EXPECT_EQ(parsed.id(), test_message_.id());
    EXPECT_EQ(parsed.name(), test_message_.name());
    EXPECT_DOUBLE_EQ(parsed.value(), test_message_.value());
}

TEST_F(ProtoExtensionsTest, TryParseValidMessage) {
    auto packet = Create(test_message_, "ProtoMessage");

    auto result = TryParse<test_proto::SimpleMessage>(packet);

    ASSERT_TRUE(result.has_value());
    EXPECT_EQ(result->id(), test_message_.id());
}

TEST_F(ProtoExtensionsTest, TryParseInvalidMessage) {
    // Create packet with invalid protobuf data
    Bytes invalid_payload = {0xFF, 0xFF, 0xFF};
    auto packet = Packet::FromBytes("InvalidMessage", std::move(invalid_payload));

    auto result = TryParse<test_proto::SimpleMessage>(packet);

    EXPECT_FALSE(result.has_value());
}

TEST_F(ProtoExtensionsTest, ParseIntoExistingMessage) {
    auto packet = Create(test_message_, "ProtoMessage");

    test_proto::SimpleMessage target;
    bool success = ParseInto(packet, target);

    ASSERT_TRUE(success);
    EXPECT_EQ(target.id(), test_message_.id());
    EXPECT_EQ(target.name(), test_message_.name());
    EXPECT_DOUBLE_EQ(target.value(), test_message_.value());
}

TEST_F(ProtoExtensionsTest, CreateFromPointer) {
    auto packet = CreateFromPointer(&test_message_, "PointerMessage");

    EXPECT_EQ(packet.GetMsgId(), "PointerMessage");
    EXPECT_FALSE(packet.GetPayload().empty());

    auto parsed = Parse<test_proto::SimpleMessage>(packet);
    EXPECT_EQ(parsed.id(), test_message_.id());
}

TEST_F(ProtoExtensionsTest, CreateFromNullPointer) {
    test_proto::SimpleMessage* null_ptr = nullptr;

    EXPECT_THROW(
        CreateFromPointer(null_ptr, "NullMessage"),
        std::invalid_argument
    );
}

TEST_F(ProtoExtensionsTest, GetByteSize) {
    size_t size = GetByteSize(test_message_);

    EXPECT_GT(size, 0);
    // The byte size should be at least as large as the data we put in
    EXPECT_GE(size, sizeof(int32_t) + test_message_.name().size() + sizeof(double));
}

TEST_F(ProtoExtensionsTest, IsValidMessage) {
    auto packet = Create(test_message_, "ValidMessage");

    bool valid = IsValid<test_proto::SimpleMessage>(packet);

    EXPECT_TRUE(valid);
}

TEST_F(ProtoExtensionsTest, IsInvalidMessage) {
    Bytes invalid_payload = {0xFF, 0xFF};
    auto packet = Packet::FromBytes("InvalidMessage", std::move(invalid_payload));

    bool valid = IsValid<test_proto::SimpleMessage>(packet);

    EXPECT_FALSE(valid);
}

TEST_F(ProtoExtensionsTest, CreateEmpty) {
    auto empty = CreateEmpty<test_proto::SimpleMessage>();

    EXPECT_EQ(empty.id(), 0);
    EXPECT_TRUE(empty.name().empty());
    EXPECT_DOUBLE_EQ(empty.value(), 0.0);
}

TEST_F(ProtoExtensionsTest, HandleEmptyMessage) {
    test_proto::SimpleMessage empty;
    auto packet = Create(empty, "EmptyMessage");

    auto parsed = Parse<test_proto::SimpleMessage>(packet);

    EXPECT_EQ(parsed.id(), 0);
    EXPECT_TRUE(parsed.name().empty());
    EXPECT_DOUBLE_EQ(parsed.value(), 0.0);
}

TEST_F(ProtoExtensionsTest, HandleMessageWithEmptyString) {
    test_proto::SimpleMessage msg;
    msg.set_id(123);
    msg.set_name("");
    msg.set_value(1.5);

    auto packet = Create(msg, "EmptyStringMessage");
    auto parsed = Parse<test_proto::SimpleMessage>(packet);

    EXPECT_EQ(parsed.id(), 123);
    EXPECT_TRUE(parsed.name().empty());
    EXPECT_DOUBLE_EQ(parsed.value(), 1.5);
}

TEST_F(ProtoExtensionsTest, HandleNegativeNumbers) {
    test_proto::SimpleMessage msg;
    msg.set_id(-999);
    msg.set_value(-123.456);

    auto packet = Create(msg, "NegativeMessage");
    auto parsed = Parse<test_proto::SimpleMessage>(packet);

    EXPECT_EQ(parsed.id(), -999);
    EXPECT_DOUBLE_EQ(parsed.value(), -123.456);
}

TEST_F(ProtoExtensionsTest, HandleLargeStrings) {
    test_proto::SimpleMessage msg;
    std::string large_string(10000, 'A');
    msg.set_name(large_string);

    auto packet = Create(msg, "LargeStringMessage");
    auto parsed = Parse<test_proto::SimpleMessage>(packet);

    EXPECT_EQ(parsed.name(), large_string);
    EXPECT_EQ(parsed.name().size(), 10000);
}

TEST_F(ProtoExtensionsTest, HandleUnicodeInStrings) {
    test_proto::SimpleMessage msg;
    msg.set_name("Hello 世界 🌍");

    auto packet = Create(msg, "UnicodeMessage");
    auto parsed = Parse<test_proto::SimpleMessage>(packet);

    EXPECT_EQ(parsed.name(), "Hello 世界 🌍");
}

TEST_F(ProtoExtensionsTest, MultipleSerializationRoundTrips) {
    auto packet1 = Create(test_message_, "Round1");
    auto parsed1 = Parse<test_proto::SimpleMessage>(packet1);

    auto packet2 = Create(parsed1, "Round2");
    auto parsed2 = Parse<test_proto::SimpleMessage>(packet2);

    auto packet3 = Create(parsed2, "Round3");
    auto parsed3 = Parse<test_proto::SimpleMessage>(packet3);

    EXPECT_EQ(parsed3.id(), test_message_.id());
    EXPECT_EQ(parsed3.name(), test_message_.name());
    EXPECT_DOUBLE_EQ(parsed3.value(), test_message_.value());
}

int main(int argc, char **argv) {
    ::testing::InitGoogleTest(&argc, argv);
    return RUN_ALL_TESTS();
}
