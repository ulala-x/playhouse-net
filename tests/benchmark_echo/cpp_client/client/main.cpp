#include "echo_client.h"
#include "../asio/asio.h"
#include <iostream>
#include <sstream>
#include <iomanip>
#include <chrono>
#include <memory>

using namespace std::literals;
using namespace playhouse;

// ========================================================================
// Platform-specific keyboard input (Linux only)
// ========================================================================

#if defined(__linux__)
#include <termios.h>
#include <unistd.h>

class KeyboardInit {
public:
    KeyboardInit()
    {
        tcgetattr(0, &initial_settings);
        new_settings = initial_settings;
        new_settings.c_lflag &= ~ICANON;
        new_settings.c_lflag &= ~ECHO;
        new_settings.c_cc[VMIN] = 1;
        new_settings.c_cc[VTIME] = 0;
        tcsetattr(0, TCSANOW, &new_settings);
    }

    ~KeyboardInit()
    {
        tcsetattr(0, TCSANOW, &initial_settings);
    }

    int kbhit()
    {
        unsigned char ch;
        int nread;

        if (peek_character != -1) return 1;
        new_settings.c_cc[VMIN] = 0;
        tcsetattr(0, TCSANOW, &new_settings);
        nread = read(0, &ch, 1);
        new_settings.c_cc[VMIN] = 1;
        tcsetattr(0, TCSANOW, &new_settings);
        if (nread == 1) {
            peek_character = ch;
            return 1;
        }
        return 0;
    }

    int getch()
    {
        char ch;

        if (peek_character != -1) {
            ch = peek_character;
            peek_character = -1;
            return ch;
        }
        int nread = read(0, &ch, 1);

        return (nread == 1) ? ch : 0;
    }

private:
    struct termios initial_settings, new_settings;
    int peek_character = -1;
};

static KeyboardInit g_keyboard;

int _kbhit() { return g_keyboard.kbhit(); }
int _getch() { return g_keyboard.getch(); }

#elif defined(_WIN32)
#include <conio.h>
#endif

// ========================================================================
// ASCII Codes
// ========================================================================

#if defined(_WIN32)
# define ASCII_CODE_ENTER       0x0d
# define ASCII_CODE_BACKSPACE   0x08
# define ASCII_CODE_ESCAPE      0x1b
#elif defined(__linux__)
# define ASCII_CODE_ENTER       0x0a
# define ASCII_CODE_BACKSPACE   0x7f
# define ASCII_CODE_ESCAPE      0x1b
#else
# define ASCII_CODE_ENTER       0x0d
# define ASCII_CODE_BACKSPACE   0x08
# define ASCII_CODE_ESCAPE      0x1b
#endif

// ========================================================================
// Global Client Instance
// ========================================================================

std::unique_ptr<EchoClient> g_echo_client;

// ========================================================================
// Display Functions
// ========================================================================

void print_title()
{
    std::cout << " ---------------------------------------------------\n";
    std::cout << "\n";
    std::cout << "\x1b[97m    PlayHouse Echo Client - C++\x1b[0m\n";
    std::cout << "\n";
    std::cout << "\n";
    std::cout << "  [command]\n";
    std::cout << "\n";
    std::cout << "   \x1b[90m- change remote endpoint\x1b[0m       'ENTER' key\n";
    std::cout << "   \x1b[90m- connect session\x1b[0m              '1'(+1), '2'(+10), '3'(+100), '4'(+1000), '5'(+10000)\n";
    std::cout << "   \x1b[90m- close session\x1b[0m                '6'(-1), '7'(all)\n";
    std::cout << "\n";
    std::cout << "   connection test\n";
    std::cout << "   \x1b[90m- start/stop\x1b[0m                   'q'\n";
    std::cout << "   \x1b[90m- increase min range\x1b[0m           'w'\n";
    std::cout << "   \x1b[90m- decrease min range\x1b[0m           'e'\n";
    std::cout << "   \x1b[90m- increase max range\x1b[0m           'r'\n";
    std::cout << "   \x1b[90m- decrease max range\x1b[0m           't'\n";
    std::cout << "\n";
    std::cout << "   traffic test\n";
    std::cout << "   \x1b[90m- start/stop\x1b[0m                   space key\n";
    std::cout << "   \x1b[90m- relay echo on/off\x1b[0m            '/'\n";
    std::cout << "   \x1b[90m- increase traffic\x1b[0m             'a'(+1), 's'(+10), 'd'(+100), 'f'(+1000), 'g'(+10000), 'h'(+100000)\n";
    std::cout << "   \x1b[90m- decrease traffic\x1b[0m             'z'(-1), 'x'(-10), 'c'(-100), 'v'(-1000), 'b'(-10000), 'n'(-100000)\n";
    std::cout << "   \x1b[90m- change message size\x1b[0m          'm'(+), 'j'(-)\n";
    std::cout << "   \x1b[90m- single send\x1b[0m                  'u'(1), 'i'(10), 'o'(100), 'p'(1000)\n";
    std::cout << "\n";
    std::cout << " ---------------------------------------------------\n";
    std::cout << "\n";
}

void print_endpoint(EchoClient* client)
{
    std::cout << " [remote endpoint]\n";
    std::cout << "\x1b[90m address : \x1b[0m" << client->get_host() << "\x1b[K\n";
    std::cout << "\x1b[90m port    : \x1b[0m" << client->get_port() << "\x1b[K\n\n";
}

void print_setting_info(EchoClient* client, std::stringstream& buf)
{
    static const char* size_names[] = {
        "8B", "64B", "256B", "1KB", "4KB", "16KB", "64KB"
    };

    buf << "\x1b[5B";

    buf << "\x1b[37m [con test]   \x1b[0m";
    buf << (client->is_connect_test_enabled() ? "\x1b[47m\x1b[30m on \x1b[0m " : "\x1b[90m off\x1b[0m ");
    buf << "\x1b[90mrange min \x1b[0m" << client->get_connect_min();
    buf << "\x1b[90m ~ max \x1b[0m" << client->get_connect_max() << "\x1b[K\n";

    buf << "\x1b[37m [echo test]  \x1b[0m";
    buf << (client->is_traffic_test_enabled() ? "\x1b[47m\x1b[30m on \x1b[0m " : "\x1b[90m off\x1b[0m ");
    buf << "\x1b[90mmessage size \x1b[0m";
    size_t idx = client->get_message_size_index();
    buf << (idx < MESSAGE_TYPE_COUNT ? size_names[idx] : "???");
    buf << ",  \x1b[90mtimes \x1b[0m" << client->get_times() << "\x1b[K\n";

    buf << "\x1b[37m [relay echo] \x1b[0m";
    buf << (client->is_relay_echo_enabled() ? "\x1b[47m\x1b[30m on \x1b[0m " : "\x1b[90m off\x1b[0m ");
    buf << "\x1b[K\n";

    buf << "\x1b[8A";
}

void print_statistics_info(EchoClient* client, bool update_now = false)
{
    static auto last_update = std::chrono::steady_clock::now();
    auto now = std::chrono::steady_clock::now();
    auto elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(now - last_update);

    if (!update_now && elapsed < 1s) {
        return;
    }

    last_update = now;

    std::stringstream buf;

    // Connection info
    buf << " [connection]";
    buf << "\x1b[90m now \x1b[0m" << client->get_connection_count();
    buf << "\x1b[K\n\n";

    // Send info
    buf << " [send]    ";
    buf << "\x1b[90m   messages \x1b[0m" << std::setw(12) << client->get_send_count();
    buf << "\x1b[90m   messages/s \x1b[0m" << std::setw(12) << std::fixed << std::setprecision(2) << client->get_send_rate();
    buf << "\x1b[90m   bytes/s \x1b[0m" << static_cast<uint64_t>(client->get_send_bytes_rate());
    buf << "\x1b[K\n";

    // Receive info
    buf << " [receive] ";
    buf << "\x1b[90m   messages \x1b[0m" << std::setw(12) << client->get_recv_count();
    buf << "\x1b[90m   messages/s \x1b[0m" << std::setw(12) << std::fixed << std::setprecision(2) << client->get_recv_rate();
    buf << "\x1b[90m   bytes/s \x1b[0m" << static_cast<uint64_t>(client->get_recv_bytes_rate());
    buf << "\x1b[K\n";
    buf << "\n";

    buf << "\x1b[5A";

    // Setting info
    print_setting_info(client, buf);

    // Write output
    const auto& str = buf.str();
    fwrite(str.c_str(), 1, str.size(), stdout);
}

std::string get_input_line()
{
    std::string input;
    int ch;

    while ((ch = _getch()) != ASCII_CODE_ENTER && input.size() < 256) {
        if (ch == ASCII_CODE_ESCAPE) {
            input.clear();
            break;
        }

        if (ch == ASCII_CODE_BACKSPACE) {
            if (input.empty())
                continue;

            input.pop_back();
            std::cout << "\b \b" << std::flush;
            continue;
        }

        if (!((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || ch == '.')) {
            continue;
        }

        input += static_cast<char>(ch);
        std::putchar(ch);
        std::cout << std::flush;
    }

    std::cout << std::endl;
    std::cout << std::flush;

    return input;
}

// ========================================================================
// Main
// ========================================================================

int main()
{
    // Hide cursor
    std::cout << "\x1b[?25l";

    // Initialize asio system
    asio::system::init_instance();

    // Print title
    print_title();

    // Create client
    g_echo_client = std::make_unique<EchoClient>();

    // Start client
    g_echo_client->start();

    // Print endpoint
    print_endpoint(g_echo_client.get());

    // Main loop
    for (;;) {
        // Display statistics
        print_statistics_info(g_echo_client.get());

        // Check keyboard input
        if (_kbhit()) {
            int ch = _getch();

            // ESC to exit
            if (ch == ASCII_CODE_ESCAPE) {
                break;
            }

            switch (ch) {
            // Connect
            case '1':
                g_echo_client->request_connect(1);
                break;
            case '2':
                g_echo_client->request_connect(10);
                break;
            case '3':
                g_echo_client->request_connect(100);
                break;
            case '4':
                g_echo_client->request_connect(1000);
                break;
            case '5':
                g_echo_client->request_connect(10000);
                break;

            // Disconnect
            case '6':
                g_echo_client->request_disconnect(1);
                break;
            case '7':
                g_echo_client->request_disconnect_all();
                break;

            // Connection test
            case 'Q':
            case 'q':
                g_echo_client->toggle_connect_test();
                break;
            case 'W':
            case 'w':
                g_echo_client->add_connect_min(100);
                break;
            case 'E':
            case 'e':
                g_echo_client->sub_connect_min(100);
                break;
            case 'R':
            case 'r':
                g_echo_client->add_connect_max(100);
                break;
            case 'T':
            case 't':
                g_echo_client->sub_connect_max(100);
                break;

            // Traffic test
            case ' ':
                g_echo_client->toggle_traffic_test();
                break;

            case 'A':
            case 'a':
                g_echo_client->add_times(1);
                break;
            case 'Z':
            case 'z':
                g_echo_client->sub_times(1);
                break;
            case 'S':
            case 's':
                g_echo_client->add_times(10);
                break;
            case 'X':
            case 'x':
                g_echo_client->sub_times(10);
                break;
            case 'D':
            case 'd':
                g_echo_client->add_times(100);
                break;
            case 'C':
            case 'c':
                g_echo_client->sub_times(100);
                break;
            case 'F':
            case 'f':
                g_echo_client->add_times(1000);
                break;
            case 'V':
            case 'v':
                g_echo_client->sub_times(1000);
                break;
            case 'G':
            case 'g':
                g_echo_client->add_times(10000);
                break;
            case 'B':
            case 'b':
                g_echo_client->sub_times(10000);
                break;
            case 'H':
            case 'h':
                g_echo_client->add_times(100000);
                break;
            case 'N':
            case 'n':
                g_echo_client->sub_times(100000);
                break;

            case 'j':
                g_echo_client->increase_message_size();
                break;
            case 'm':
                g_echo_client->decrease_message_size();
                break;

            case 'U':
            case 'u':
                g_echo_client->request_send_immediately(1);
                break;
            case 'I':
            case 'i':
                g_echo_client->request_send_immediately(10);
                break;
            case 'O':
            case 'o':
                g_echo_client->request_send_immediately(100);
                break;
            case 'P':
            case 'p':
                g_echo_client->request_send_immediately(1000);
                break;

            case '/':
                g_echo_client->toggle_relay_echo();
                break;

            case ASCII_CODE_ENTER:
                {
                    std::cout << "\x1b[3A";
                    std::cout << "\x1b[2K";
                    std::cout << "\x1b[90m address : \x1b[0m" << std::flush;
                    auto address = get_input_line();

                    std::cout << "\x1b[2K";
                    std::cout << "\x1b[90m port    : \x1b[0m" << std::flush;
                    auto port_str = get_input_line();

                    if (!address.empty() && !port_str.empty()) {
                        try {
                            int port = std::stoi(port_str);
                            g_echo_client->set_endpoint(address, port);
                        } catch (...) {
                            // Invalid port
                        }
                    } else if (!address.empty()) {
                        g_echo_client->set_endpoint(address, g_echo_client->get_port());
                    } else if (!port_str.empty()) {
                        try {
                            int port = std::stoi(port_str);
                            g_echo_client->set_endpoint(g_echo_client->get_host(), port);
                        } catch (...) {
                            // Invalid port
                        }
                    }

                    std::cout << "\x1b[3A";
                    print_endpoint(g_echo_client.get());
                }
                break;

            case ASCII_CODE_BACKSPACE:
                {
                    std::cout << "\033c";
                    print_title();
                    print_endpoint(g_echo_client.get());
                    print_statistics_info(g_echo_client.get(), true);
                }
                break;
            }

            // Update display
            std::stringstream buf;
            buf << "\x1b[5B";
            print_setting_info(g_echo_client.get(), buf);
            const auto& str = buf.str();
            fwrite(str.c_str(), 1, str.size(), stdout);
        }

        // Sleep
        std::this_thread::sleep_for(100ms);
    }

    // Cleanup
    g_echo_client.reset();

    std::cout << "\x1b[7B\n PlayHouse echo client closed... \n";

    // Destroy asio system
    asio::system::destroy_instance();

    // Show cursor
    std::cout << "\x1b[?25h";

    return 0;
}
