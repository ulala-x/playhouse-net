package examples;

import com.playhouse.connector.Connector;
import com.playhouse.connector.ConnectorConfig;
import com.playhouse.connector.ConnectorErrorCode;
import com.playhouse.connector.Packet;

import java.nio.charset.StandardCharsets;

/**
 * WebSocket 연결 예제
 * <p>
 * Java Connector에서 WebSocket 프로토콜을 사용하는 방법을 보여줍니다.
 */
public class WebSocketExample {

    public static void main(String[] args) {
        // Example 1: TCP Connection (default)
        System.out.println("=== TCP Connection Example ===");
        tcpExample();

        // Example 2: WebSocket Connection
        System.out.println("\n=== WebSocket Connection Example ===");
        webSocketExample();

        // Example 3: WebSocket with SSL
        System.out.println("\n=== WebSocket with SSL Example ===");
        webSocketSslExample();

        // Example 4: Callback-based Authentication
        System.out.println("\n=== Callback Authentication Example ===");
        callbackAuthExample();
    }

    /**
     * TCP 연결 예제
     */
    private static void tcpExample() {
        ConnectorConfig config = ConnectorConfig.builder()
            .requestTimeoutMs(10000)
            .heartbeatIntervalMs(10000)
            .build();

        try (Connector connector = new Connector()) {
            connector.init(config);

            // Set event callbacks
            connector.setOnConnect(() -> System.out.println("Connected via TCP"));
            connector.setOnDisconnect(() -> System.out.println("Disconnected"));
            connector.setOnReceive(packet -> System.out.println("Received: " + packet));
            connector.setOnError((code, msg) -> System.err.println("Error: " + code + " - " + msg));

            // Connect to server
            connector.connectAsync("localhost", 34001).join();

            // Authenticate
            byte[] authData = "user123".getBytes(StandardCharsets.UTF_8);
            boolean authenticated = connector.authenticateAsync("gameService", "user123", authData).join();
            System.out.println("Authenticated: " + authenticated);

            // Send request
            Packet request = Packet.fromBytes("EchoRequest", "Hello TCP".getBytes());
            Packet response = connector.requestAsync(request).join();
            System.out.println("Response: " + new String(response.getPayload()));

            // Disconnect
            connector.disconnect();

        } catch (Exception e) {
            System.err.println("TCP example failed: " + e.getMessage());
        }
    }

    /**
     * WebSocket 연결 예제
     */
    private static void webSocketExample() {
        ConnectorConfig config = ConnectorConfig.builder()
            .useWebsocket(true)
            .webSocketPath("/ws")
            .requestTimeoutMs(10000)
            .build();

        try (Connector connector = new Connector()) {
            connector.init(config);

            // Set event callbacks
            connector.setOnConnect(() -> System.out.println("Connected via WebSocket"));
            connector.setOnDisconnect(() -> System.out.println("Disconnected"));
            connector.setOnReceive(packet -> System.out.println("Received: " + packet));
            connector.setOnError((code, msg) -> System.err.println("Error: " + code + " - " + msg));

            // Connect to server
            connector.connectAsync("localhost", 38080).join();

            // Authenticate
            byte[] authData = "user456".getBytes(StandardCharsets.UTF_8);
            boolean authenticated = connector.authenticateAsync("gameService", "user456", authData).join();
            System.out.println("Authenticated: " + authenticated);

            // Send request
            Packet request = Packet.fromBytes("EchoRequest", "Hello WebSocket".getBytes());
            Packet response = connector.requestAsync(request).join();
            System.out.println("Response: " + new String(response.getPayload()));

            // Check for errors using ConnectorErrorCode
            if (response.hasError()) {
                ConnectorErrorCode errorCode = ConnectorErrorCode.fromCode(response.getErrorCode());
                System.err.println("Response error: " + errorCode);
            }

            // Disconnect
            connector.disconnect();

        } catch (Exception e) {
            System.err.println("WebSocket example failed: " + e.getMessage());
        }
    }

    /**
     * WebSocket + SSL 연결 예제
     */
    private static void webSocketSslExample() {
        ConnectorConfig config = ConnectorConfig.builder()
            .useWebsocket(true)
            .useSsl(true)
            .webSocketPath("/ws")
            .skipServerCertificateValidation(true) // 테스트용! 프로덕션에서는 false
            .requestTimeoutMs(10000)
            .build();

        try (Connector connector = new Connector()) {
            connector.init(config);

            // Set event callbacks
            connector.setOnConnect(() -> System.out.println("Connected via WebSocket (SSL)"));
            connector.setOnDisconnect(() -> System.out.println("Disconnected"));

            // Connect to server
            connector.connectAsync("secure.example.com", 443).join();

            System.out.println("Successfully connected with SSL!");

            // Disconnect
            connector.disconnect();

        } catch (Exception e) {
            System.err.println("WebSocket SSL example failed: " + e.getMessage());
        }
    }

    /**
     * 콜백 기반 인증 예제
     */
    private static void callbackAuthExample() {
        ConnectorConfig config = ConnectorConfig.builder()
            .useWebsocket(true)
            .webSocketPath("/ws")
            .build();

        try (Connector connector = new Connector()) {
            connector.init(config);

            connector.setOnConnect(() -> {
                System.out.println("Connected! Starting authentication...");

                // Callback-based authentication
                byte[] authData = "user789".getBytes(StandardCharsets.UTF_8);
                connector.authenticate("gameService", "user789", authData, success -> {
                    if (success) {
                        System.out.println("✓ Authentication successful!");

                        // Send a request after authentication
                        Packet request = Packet.fromBytes("GetUserInfo", new byte[0]);
                        connector.request(request, response -> {
                            System.out.println("✓ Got user info: " + response);
                        });
                    } else {
                        System.err.println("✗ Authentication failed!");
                    }
                });
            });

            connector.connectAsync("localhost", 38080).join();

            // Keep alive for callbacks
            Thread.sleep(2000);

            connector.disconnect();

        } catch (Exception e) {
            System.err.println("Callback auth example failed: " + e.getMessage());
        }
    }

    /**
     * 에러 처리 예제
     */
    private static void errorHandlingExample() {
        ConnectorConfig config = ConnectorConfig.builder()
            .useWebsocket(true)
            .requestTimeoutMs(5000)
            .build();

        try (Connector connector = new Connector()) {
            connector.init(config);

            connector.setOnError((code, msg) -> {
                ConnectorErrorCode errorCode = ConnectorErrorCode.fromCode(code);
                if (errorCode != null) {
                    System.err.println("Standard error: " + errorCode);
                } else {
                    System.err.println("Custom error: " + code + " - " + msg);
                }
            });

            connector.connectAsync("localhost", 38080).join();

            // Test timeout
            Packet request = Packet.fromBytes("SlowRequest", new byte[0]);
            try {
                connector.requestAsync(request).join();
            } catch (Exception e) {
                System.err.println("Request failed: " + e.getMessage());
            }

        } catch (Exception e) {
            System.err.println("Error handling example failed: " + e.getMessage());
        }
    }
}
