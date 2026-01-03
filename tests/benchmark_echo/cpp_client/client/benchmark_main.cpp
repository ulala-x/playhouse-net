#include "echo_client.h"
#include "../asio/asio.h"
#include <iostream>
#include <chrono>
#include <memory>
#include <thread>
#include <iomanip>

using namespace std::literals;
using namespace playhouse;

// ASIO system initialization guard
class AsioSystemGuard {
public:
    AsioSystemGuard() {
        asio::system::init_instance();
    }
    ~AsioSystemGuard() {
        asio::system::destroy_instance();
    }
};

void print_usage(const char* program_name)
{
    std::cout << "Usage: " << program_name << " <host> <port> <connections> <message_size> <times> <duration>\n";
    std::cout << "  host:         Server host (e.g., localhost, 127.0.0.1)\n";
    std::cout << "  port:         Server port (e.g., 16110)\n";
    std::cout << "  connections:  Number of concurrent connections (e.g., 1000)\n";
    std::cout << "  message_size: Message size in bytes (8, 64, 256, 1024, etc.)\n";
    std::cout << "  times:        Number of echo requests per connection (e.g., 200)\n";
    std::cout << "  duration:     Test duration in seconds (e.g., 10)\n";
    std::cout << "\nExample:\n";
    std::cout << "  " << program_name << " localhost 16110 1000 64 200 10\n";
}

size_t get_message_size_index(size_t message_size)
{
    const size_t sizes[] = {8, 64, 256, 1024, 4096, 16384, 65536};

    for (size_t i = 0; i < MESSAGE_TYPE_COUNT; ++i) {
        if (sizes[i] == message_size) {
            return i;
        }
    }

    // Default to 64 bytes if not found
    return 1;
}

int main(int argc, char* argv[])
{
    // Initialize ASIO system
    AsioSystemGuard asio_guard;

    // Parse command line arguments
    if (argc != 7) {
        print_usage(argv[0]);
        return 1;
    }

    try {
        std::string host = argv[1];
        int port = std::stoi(argv[2]);
        int connections = std::stoi(argv[3]);
        size_t message_size = std::stoull(argv[4]);
        int times = std::stoi(argv[5]);
        int duration_sec = std::stoi(argv[6]);

        // Validate inputs
        if (port <= 0 || port > 65535) {
            std::cerr << "Error: Invalid port number\n";
            return 1;
        }

        if (connections <= 0 || connections > 100000) {
            std::cerr << "Error: Invalid connection count (must be 1-100000)\n";
            return 1;
        }

        if (times <= 0) {
            std::cerr << "Error: Invalid times value\n";
            return 1;
        }

        if (duration_sec <= 0) {
            std::cerr << "Error: Invalid duration\n";
            return 1;
        }

        // Get message size index
        size_t msg_idx = get_message_size_index(message_size);

        // Print configuration
        std::cout << "\n========================================\n";
        std::cout << "PlayHouse C++ Echo Benchmark\n";
        std::cout << "========================================\n";
        std::cout << "Server:       " << host << ":" << port << "\n";
        std::cout << "Connections:  " << connections << "\n";
        std::cout << "Message Size: " << message_size << " bytes\n";
        std::cout << "Times:        " << times << " per connection\n";
        std::cout << "Duration:     " << duration_sec << " seconds\n";
        std::cout << "========================================\n\n";

        // Create and start client
        auto client = std::make_unique<EchoClient>();
        client->set_endpoint(host, port);
        client->start();

        std::cout << "Starting client...\n";
        std::this_thread::sleep_for(1s);

        // Connect sessions
        std::cout << "Connecting " << connections << " sessions...\n";
        client->request_connect(connections);

        // Wait for connections to establish
        std::cout << "Waiting for connections to establish...\n";
        for (int i = 0; i < 20; ++i) {
            std::this_thread::sleep_for(500ms);
            size_t conn_count = client->get_connection_count();
            std::cout << "\r  Connected: " << conn_count << " / " << connections << std::flush;
            if (conn_count >= static_cast<size_t>(connections * 0.95)) {
                break;
            }
        }
        std::cout << "\n";

        // Set message size and times
        for (size_t i = 0; i < MESSAGE_TYPE_COUNT; ++i) {
            if (i == msg_idx) break;
            client->decrease_message_size();
        }

        // Set times
        client->add_times(times);

        // Print connection status
        std::cout << "Connected sessions: " << client->get_connection_count() << "\n\n";

        if (client->get_connection_count() == 0) {
            std::cerr << "Error: Failed to establish connections\n";
            return 1;
        }

        // Start traffic test
        std::cout << "Starting traffic test...\n";
        client->toggle_traffic_test();

        // Run for specified duration
        auto start_time = std::chrono::steady_clock::now();
        auto last_print = start_time;

        while (true) {
            auto now = std::chrono::steady_clock::now();
            auto elapsed = std::chrono::duration_cast<std::chrono::seconds>(now - start_time);

            if (elapsed.count() >= duration_sec) {
                break;
            }

            // Print statistics every second
            auto print_elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(now - last_print);
            if (print_elapsed >= 1s) {
                last_print = now;

                std::cout << "\r[" << std::setw(2) << elapsed.count() << "s] "
                          << "Connections: " << std::setw(5) << client->get_connection_count() << " | "
                          << "Sent: " << std::setw(10) << client->get_send_count() << " msgs "
                          << "(" << std::setw(8) << std::fixed << std::setprecision(2)
                          << client->get_send_rate() << " msg/s, "
                          << std::setw(10) << static_cast<uint64_t>(client->get_send_bytes_rate()) << " B/s) | "
                          << "Recv: " << std::setw(10) << client->get_recv_count() << " msgs "
                          << "(" << std::setw(8) << std::fixed << std::setprecision(2)
                          << client->get_recv_rate() << " msg/s, "
                          << std::setw(10) << static_cast<uint64_t>(client->get_recv_bytes_rate()) << " B/s)"
                          << std::flush;
            }

            std::this_thread::sleep_for(100ms);
        }

        std::cout << "\n\n";

        // Stop traffic test
        client->toggle_traffic_test();

        // Wait for pending messages
        std::cout << "Stopping test and waiting for pending messages...\n";
        std::this_thread::sleep_for(2s);

        // Print final statistics
        std::cout << "\n========================================\n";
        std::cout << "Benchmark Results\n";
        std::cout << "========================================\n";
        std::cout << "Duration:        " << duration_sec << " seconds\n";
        std::cout << "Connections:     " << client->get_connection_count() << "\n";
        std::cout << "\nMessages Sent:\n";
        std::cout << "  Total:         " << client->get_send_count() << " messages\n";
        std::cout << "  Throughput:    " << std::fixed << std::setprecision(2)
                  << client->get_send_rate() << " msg/s\n";
        std::cout << "  Bandwidth:     " << static_cast<uint64_t>(client->get_send_bytes_rate())
                  << " bytes/s (" << std::fixed << std::setprecision(2)
                  << (client->get_send_bytes_rate() / 1024.0 / 1024.0) << " MB/s)\n";
        std::cout << "\nMessages Received:\n";
        std::cout << "  Total:         " << client->get_recv_count() << " messages\n";
        std::cout << "  Throughput:    " << std::fixed << std::setprecision(2)
                  << client->get_recv_rate() << " msg/s\n";
        std::cout << "  Bandwidth:     " << static_cast<uint64_t>(client->get_recv_bytes_rate())
                  << " bytes/s (" << std::fixed << std::setprecision(2)
                  << (client->get_recv_bytes_rate() / 1024.0 / 1024.0) << " MB/s)\n";
        std::cout << "\nSuccess Rate:    " << std::fixed << std::setprecision(2)
                  << (client->get_send_count() > 0 ?
                      (static_cast<double>(client->get_recv_count()) / client->get_send_count() * 100.0) : 0.0)
                  << "%\n";
        std::cout << "========================================\n\n";

        // Disconnect all
        std::cout << "Disconnecting all sessions...\n";
        client->request_disconnect_all();
        std::this_thread::sleep_for(1s);

        // Cleanup
        client.reset();

        std::cout << "Benchmark completed successfully\n";

        return 0;

    } catch (const std::exception& e) {
        std::cerr << "Error: " << e.what() << "\n";
        return 1;
    }
}
