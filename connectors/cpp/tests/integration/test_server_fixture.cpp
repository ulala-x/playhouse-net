#include "test_server_fixture.hpp"
#include <cstdlib>
#include <sstream>
#include <stdexcept>
#include <atomic>

// We'll use a simple HTTP client approach
// For production, consider using a proper HTTP library like libcurl or cpp-httplib

#ifdef _WIN32
    #include <winsock2.h>
    #include <ws2tcpip.h>
    #pragma comment(lib, "Ws2_32.lib")
#else
    #include <sys/socket.h>
    #include <netinet/in.h>
    #include <arpa/inet.h>
    #include <netdb.h>
    #include <unistd.h>
    #define closesocket close
#endif

namespace playhouse::test {

namespace {
    std::atomic<int> stage_id_counter{1};

    std::string GetEnvOrDefault(const char* name, const char* default_value) {
        const char* value = std::getenv(name);
        return value ? std::string(value) : std::string(default_value);
    }

    // Simple JSON parser for test server responses
    std::string ExtractJsonString(const std::string& json, const std::string& key) {
        auto key_pos = json.find("\"" + key + "\"");
        if (key_pos == std::string::npos) return "";

        auto colon_pos = json.find(":", key_pos);
        if (colon_pos == std::string::npos) return "";

        auto quote1 = json.find("\"", colon_pos);
        if (quote1 == std::string::npos) return "";

        auto quote2 = json.find("\"", quote1 + 1);
        if (quote2 == std::string::npos) return "";

        return json.substr(quote1 + 1, quote2 - quote1 - 1);
    }

    int64_t ExtractJsonInt(const std::string& json, const std::string& key) {
        auto key_pos = json.find("\"" + key + "\"");
        if (key_pos == std::string::npos) return 0;

        auto colon_pos = json.find(":", key_pos);
        if (colon_pos == std::string::npos) return 0;

        // Skip whitespace
        auto start = colon_pos + 1;
        while (start < json.size() && (json[start] == ' ' || json[start] == '\t')) {
            start++;
        }

        // Find end (comma, brace, or end of string)
        auto end = start;
        while (end < json.size() && json[end] != ',' && json[end] != '}' && json[end] != '\n') {
            end++;
        }

        if (start >= end) return 0;

        std::string num_str = json.substr(start, end - start);
        try {
            return std::stoll(num_str);
        } catch (...) {
            return 0;
        }
    }

    bool ExtractJsonBool(const std::string& json, const std::string& key) {
        auto key_pos = json.find("\"" + key + "\"");
        if (key_pos == std::string::npos) return false;

        auto colon_pos = json.find(":", key_pos);
        if (colon_pos == std::string::npos) return false;

        auto true_pos = json.find("true", colon_pos);
        auto false_pos = json.find("false", colon_pos);

        if (true_pos != std::string::npos && (false_pos == std::string::npos || true_pos < false_pos)) {
            return true;
        }

        return false;
    }

    // Simple HTTP POST request
    std::string HttpPost(const std::string& host, uint16_t port, const std::string& path, const std::string& body) {
#ifdef _WIN32
        static bool wsa_initialized = false;
        if (!wsa_initialized) {
            WSADATA wsaData;
            WSAStartup(MAKEWORD(2, 2), &wsaData);
            wsa_initialized = true;
        }
#endif

        // Create socket
        int sock = socket(AF_INET, SOCK_STREAM, 0);
        if (sock < 0) {
            throw std::runtime_error("Failed to create socket");
        }

        // Resolve host
        struct hostent* server = gethostbyname(host.c_str());
        if (!server) {
            closesocket(sock);
            throw std::runtime_error("Failed to resolve host: " + host);
        }

        // Connect to server
        struct sockaddr_in server_addr;
        server_addr.sin_family = AF_INET;
        server_addr.sin_port = htons(port);
        memcpy(&server_addr.sin_addr.s_addr, server->h_addr, server->h_length);

        if (connect(sock, (struct sockaddr*)&server_addr, sizeof(server_addr)) < 0) {
            closesocket(sock);
            throw std::runtime_error("Failed to connect to server");
        }

        // Build HTTP request
        std::ostringstream request;
        request << "POST " << path << " HTTP/1.1\r\n";
        request << "Host: " << host << ":" << port << "\r\n";
        request << "Content-Type: application/json\r\n";
        request << "Content-Length: " << body.size() << "\r\n";
        request << "Connection: close\r\n";
        request << "\r\n";
        request << body;

        std::string request_str = request.str();

        // Send request
        if (send(sock, request_str.c_str(), request_str.size(), 0) < 0) {
            closesocket(sock);
            throw std::runtime_error("Failed to send HTTP request");
        }

        // Receive response
        std::string response;
        char buffer[4096];
        int bytes_received;

        while ((bytes_received = recv(sock, buffer, sizeof(buffer) - 1, 0)) > 0) {
            buffer[bytes_received] = '\0';
            response += buffer;
        }

        closesocket(sock);

        // Extract JSON body (skip HTTP headers)
        auto body_start = response.find("\r\n\r\n");
        if (body_start != std::string::npos) {
            return response.substr(body_start + 4);
        }

        return response;
    }
}

class TestServerFixture::Impl {
public:
    Impl() = default;
    ~Impl() = default;
};

TestServerFixture::TestServerFixture()
    : impl_(std::make_unique<Impl>())
    , host_(GetEnvOrDefault("TEST_SERVER_HOST", "localhost"))
    , tcp_port_(static_cast<uint16_t>(std::stoi(GetEnvOrDefault("TEST_SERVER_TCP_PORT", "34001"))))
    , http_port_(static_cast<uint16_t>(std::stoi(GetEnvOrDefault("TEST_SERVER_HTTP_PORT", "8080"))))
    , ws_port_(static_cast<uint16_t>(std::stoi(GetEnvOrDefault("TEST_SERVER_WS_PORT", "8080"))))
{
}

TestServerFixture::~TestServerFixture() = default;

CreateStageResponse TestServerFixture::CreateStage(const std::string& stage_type, std::optional<int> max_players) {
    int64_t stage_id = stage_id_counter.fetch_add(1);

    // Build JSON request
    std::ostringstream json_body;
    json_body << "{\"stageType\":\"" << stage_type << "\",\"stageId\":" << stage_id;
    if (max_players.has_value()) {
        json_body << ",\"maxPlayers\":" << max_players.value();
    }
    json_body << "}";

    try {
        // Make HTTP POST request
        std::string response = HttpPost(host_, http_port_, "/api/stages", json_body.str());

        // Parse response
        CreateStageResponse result;
        result.success = ExtractJsonBool(response, "success");
        result.stage_id = ExtractJsonInt(response, "stageId");
        result.stage_type = stage_type;
        result.reply_payload_id = ExtractJsonString(response, "replyPayloadId");

        if (!result.success) {
            throw std::runtime_error("Stage creation failed on server");
        }

        return result;
    } catch (const std::exception& e) {
        throw std::runtime_error(std::string("Failed to create stage: ") + e.what());
    }
}

CreateStageResponse TestServerFixture::CreateTestStage() {
    return CreateStage("TestStage");
}

} // namespace playhouse::test
