#include "playhouse_codec.h"
#include "packet.h"
#include <iostream>
#include <iomanip>
#include <cassert>

using namespace playhouse;

void print_hex(const std::vector<uint8_t>& data) {
    for (size_t i = 0; i < data.size(); ++i) {
        std::cout << std::hex << std::setw(2) << std::setfill('0')
                  << static_cast<int>(data[i]) << " ";
        if ((i + 1) % 16 == 0) std::cout << "\n";
    }
    std::cout << std::dec << "\n";
}

void test_encode_request() {
    std::cout << "=== Test 1: Encode Request Packet ===\n";

    const std::string msg_id = "EchoRequest";
    const uint16_t msg_seq = 42;
    const int64_t stage_id = 12345678;
    const std::vector<uint8_t> payload = {0x01, 0x02, 0x03, 0x04};

    auto encoded = Codec::encode_request(msg_id, msg_seq, stage_id,
                                         payload.data(), payload.size());

    std::cout << "Encoded packet (" << encoded.size() << " bytes):\n";
    print_hex(encoded);

    // Verify structure
    // [Length: 4B] [MsgIdLen: 1B] [MsgId: 11B] [MsgSeq: 2B] [StageId: 8B] [Payload: 4B]
    // Expected length: 1 + 11 + 2 + 8 + 4 = 26 bytes
    assert(encoded.size() == 4 + 26);  // 4 (length prefix) + 26 (body)

    // Check length prefix
    int32_t length = Codec::read_int32_le(encoded.data());
    std::cout << "Body length: " << length << " (expected: 26)\n";
    assert(length == 26);

    // Check MsgIdLen
    std::cout << "MsgIdLen: " << static_cast<int>(encoded[4])
              << " (expected: " << msg_id.size() << ")\n";
    assert(encoded[4] == msg_id.size());

    std::cout << "Test 1 PASSED\n\n";
}

void test_decode_response() {
    std::cout << "=== Test 2: Decode Response Packet ===\n";

    // Simulate a response packet (without length prefix)
    // [MsgIdLen: 1B] [MsgId: 9B] [MsgSeq: 2B] [StageId: 8B] [ErrorCode: 2B] [OriginalSize: 4B] [Payload: 4B]
    std::vector<uint8_t> response_data;

    const std::string msg_id = "EchoReply";
    const uint16_t msg_seq = 42;
    const int64_t stage_id = 12345678;
    const uint16_t error_code = 0;
    const int32_t original_size = 0;  // No compression
    const std::vector<uint8_t> payload = {0x0A, 0x0B, 0x0C, 0x0D};

    // Build response packet manually
    response_data.push_back(static_cast<uint8_t>(msg_id.size()));  // MsgIdLen
    response_data.insert(response_data.end(), msg_id.begin(), msg_id.end());  // MsgId

    // MsgSeq
    uint8_t seq_buf[2];
    Codec::write_uint16_le(seq_buf, msg_seq);
    response_data.insert(response_data.end(), seq_buf, seq_buf + 2);

    // StageId
    uint8_t stage_buf[8];
    Codec::write_int64_le(stage_buf, stage_id);
    response_data.insert(response_data.end(), stage_buf, stage_buf + 8);

    // ErrorCode
    uint8_t err_buf[2];
    Codec::write_uint16_le(err_buf, error_code);
    response_data.insert(response_data.end(), err_buf, err_buf + 2);

    // OriginalSize
    uint8_t orig_buf[4];
    Codec::write_int32_le(orig_buf, original_size);
    response_data.insert(response_data.end(), orig_buf, orig_buf + 4);

    // Payload
    response_data.insert(response_data.end(), payload.begin(), payload.end());

    std::cout << "Response packet (" << response_data.size() << " bytes):\n";
    print_hex(response_data);

    // Decode
    std::string decoded_msg_id;
    uint16_t decoded_msg_seq;
    int64_t decoded_stage_id;
    uint16_t decoded_error_code;
    std::vector<uint8_t> decoded_payload;

    bool success = Codec::decode_response(
        response_data.data(), response_data.size(),
        decoded_msg_id, decoded_msg_seq, decoded_stage_id,
        decoded_error_code, decoded_payload);

    assert(success);
    assert(decoded_msg_id == msg_id);
    assert(decoded_msg_seq == msg_seq);
    assert(decoded_stage_id == stage_id);
    assert(decoded_error_code == error_code);
    assert(decoded_payload == payload);

    std::cout << "Decoded:\n";
    std::cout << "  MsgId: " << decoded_msg_id << "\n";
    std::cout << "  MsgSeq: " << decoded_msg_seq << "\n";
    std::cout << "  StageId: " << decoded_stage_id << "\n";
    std::cout << "  ErrorCode: " << decoded_error_code << "\n";
    std::cout << "  Payload size: " << decoded_payload.size() << "\n";

    std::cout << "Test 2 PASSED\n\n";
}

void test_packet_wrapper() {
    std::cout << "=== Test 3: Packet Wrapper Class ===\n";

    // Create request packet
    const std::string msg_id = "TestRequest";
    const uint16_t msg_seq = 100;
    const int64_t stage_id = 9999;
    const std::vector<uint8_t> payload = {0xAA, 0xBB, 0xCC};

    Packet request(msg_id, msg_seq, stage_id, payload);

    std::cout << "Request packet:\n";
    std::cout << "  MsgId: " << request.msg_id() << "\n";
    std::cout << "  MsgSeq: " << request.msg_seq() << "\n";
    std::cout << "  StageId: " << request.stage_id() << "\n";
    std::cout << "  Size: " << request.size() << " bytes\n";

    assert(request.msg_id() == msg_id);
    assert(request.msg_seq() == msg_seq);
    assert(request.stage_id() == stage_id);

    // Parse response
    std::vector<uint8_t> response_data;
    const std::string reply_id = "TestReply";

    response_data.push_back(static_cast<uint8_t>(reply_id.size()));
    response_data.insert(response_data.end(), reply_id.begin(), reply_id.end());

    uint8_t seq_buf[2];
    Codec::write_uint16_le(seq_buf, msg_seq);
    response_data.insert(response_data.end(), seq_buf, seq_buf + 2);

    uint8_t stage_buf[8];
    Codec::write_int64_le(stage_buf, stage_id);
    response_data.insert(response_data.end(), stage_buf, stage_buf + 8);

    uint8_t err_buf[2];
    Codec::write_uint16_le(err_buf, 0);
    response_data.insert(response_data.end(), err_buf, err_buf + 2);

    uint8_t orig_buf[4];
    Codec::write_int32_le(orig_buf, 0);
    response_data.insert(response_data.end(), orig_buf, orig_buf + 4);

    response_data.insert(response_data.end(), payload.begin(), payload.end());

    auto response = Packet::parse_response(response_data.data(), response_data.size());
    assert(response != nullptr);
    assert(response->msg_id() == reply_id);
    assert(response->msg_seq() == msg_seq);
    assert(response->stage_id() == stage_id);
    assert(response->is_success());
    assert(!response->has_error());

    std::cout << "Response packet:\n";
    std::cout << "  MsgId: " << response->msg_id() << "\n";
    std::cout << "  MsgSeq: " << response->msg_seq() << "\n";
    std::cout << "  StageId: " << response->stage_id() << "\n";
    std::cout << "  ErrorCode: " << response->error_code() << "\n";
    std::cout << "  Success: " << (response->is_success() ? "true" : "false") << "\n";

    std::cout << "Test 3 PASSED\n\n";
}

int main() {
    try {
        test_encode_request();
        test_decode_response();
        test_packet_wrapper();

        std::cout << "===================\n";
        std::cout << "ALL TESTS PASSED!\n";
        std::cout << "===================\n";

        return 0;
    }
    catch (const std::exception& e) {
        std::cerr << "TEST FAILED: " << e.what() << "\n";
        return 1;
    }
}
