package com.playhouse.connector;

import com.playhouse.connector.internal.ClientNetwork;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.util.concurrent.CompletableFuture;
import java.util.function.BiConsumer;
import java.util.function.Consumer;

/**
 * PlayHouse Connector
 * <p>
 * PlayHouse 실시간 게임 서버에 연결하여 메시지 송수신을 처리합니다.
 * Java NIO 기반 비동기 I/O를 사용하며, CompletableFuture로 비동기 작업을 지원합니다.
 * <p>
 * 사용 예제:
 * <pre>
 * ConnectorConfig config = ConnectorConfig.builder()
 *     .requestTimeoutMs(10000)
 *     .build();
 *
 * try (Connector connector = new Connector()) {
 *     connector.init(config);
 *     connector.setOnConnect(success -> System.out.println("Connected: " + success));
 *     connector.setOnReceive(packet -> System.out.println("Received: " + packet.getMsgId()));
 *
 *     connector.connectAsync("localhost", 34001).join();
 *
 *     // 인증
 *     Packet authPacket = Packet.fromBytes("AuthRequest", authData);
 *     boolean authenticated = connector.authenticateAsync("game", "user123", authPacket).join();
 *
 *     // 요청-응답
 *     Packet request = Packet.fromBytes("EchoRequest", "Hello".getBytes());
 *     Packet response = connector.requestAsync(request).join();
 * }
 * </pre>
 */
public final class Connector implements AutoCloseable {

    private static final Logger logger = LoggerFactory.getLogger(Connector.class);

    private ClientNetwork clientNetwork;
    private ConnectorConfig config;
    private volatile long stageId;
    private volatile boolean initialized;

    /**
     * Connector 초기화
     *
     * @param config 설정
     * @throws IllegalStateException 이미 초기화된 경우
     */
    public void init(ConnectorConfig config) {
        if (initialized) {
            throw new IllegalStateException("Connector already initialized");
        }

        if (config == null) {
            throw new IllegalArgumentException("Config cannot be null");
        }

        this.config = config;
        this.clientNetwork = new ClientNetwork(config);
        this.stageId = 0L;
        this.initialized = true;

        logger.info("Connector initialized: {}", config);
    }

    /**
     * 기본 설정으로 초기화
     */
    public void init() {
        init(ConnectorConfig.defaultConfig());
    }

    /**
     * 서버에 비동기 연결
     *
     * @param host 서버 호스트
     * @param port 서버 포트
     * @return 연결 성공 여부 Future
     * @throws IllegalStateException 초기화되지 않은 경우
     */
    public CompletableFuture<Void> connectAsync(String host, int port) {
        checkInitialized();

        logger.info("Connecting to {}:{}...", host, port);

        return clientNetwork.connectAsync(host, port)
            .thenApply(success -> {
                if (!success) {
                    throw new ConnectorException(1001, "Connection failed");
                }
                return null;
            });
    }

    /**
     * 연결 해제
     */
    public void disconnect() {
        if (!initialized) {
            return;
        }

        logger.info("Disconnecting...");
        clientNetwork.disconnectAsync().join();
    }

    /**
     * 연결 상태 확인
     *
     * @return 연결되어 있으면 true
     */
    public boolean isConnected() {
        return initialized && clientNetwork.isConnected();
    }

    /**
     * 인증 상태 확인
     *
     * @return 인증되어 있으면 true
     */
    public boolean isAuthenticated() {
        return initialized && clientNetwork.isAuthenticated();
    }

    /**
     * 인증 요청 (간편 API)
     *
     * @param serviceId 서비스 ID
     * @param accountId 계정 ID
     * @param payload   인증 페이로드
     * @return 인증 성공 여부 Future
     */
    public CompletableFuture<Boolean> authenticateAsync(String serviceId, String accountId, byte[] payload) {
        // 실제 구현에서는 AuthRequest 프로토콜 사용
        Packet authPacket = Packet.builder("AuthRequest")
            .payload(payload)
            .build();

        return requestAsync(authPacket)
            .thenApply(response -> {
                boolean success = response.getErrorCode() == 0;
                if (success) {
                    logger.info("Authentication successful: serviceId={}, accountId={}", serviceId, accountId);
                } else {
                    logger.warn("Authentication failed: errorCode={}", response.getErrorCode());
                }
                return success;
            });
    }

    /**
     * 메시지 전송 (응답 없음)
     *
     * @param packet 전송할 패킷
     * @throws IllegalStateException 초기화되지 않았거나 연결되지 않은 경우
     */
    public void send(Packet packet) {
        checkConnected();
        clientNetwork.send(packet, stageId);
    }

    /**
     * 요청 전송 (비동기 응답)
     *
     * @param packet 요청 패킷
     * @return 응답 패킷 Future
     * @throws IllegalStateException 초기화되지 않았거나 연결되지 않은 경우
     */
    public CompletableFuture<Packet> requestAsync(Packet packet) {
        checkConnected();
        return clientNetwork.requestAsync(packet, stageId);
    }

    /**
     * 요청 전송 (콜백 방식)
     *
     * @param packet   요청 패킷
     * @param callback 응답 콜백
     * @throws IllegalStateException 초기화되지 않았거나 연결되지 않은 경우
     */
    public void request(Packet packet, Consumer<Packet> callback) {
        requestAsync(packet)
            .thenAccept(callback)
            .exceptionally(e -> {
                logger.error("Request failed: {}", packet.getMsgId(), e);
                return null;
            });
    }

    /**
     * 메인 스레드에서 콜백 실행
     * <p>
     * Unity, Godot 등의 게임 엔진에서 메인 스레드에서 호출해야 합니다.
     * 일반 서버 애플리케이션에서는 필요하지 않습니다.
     */
    public void mainThreadAction() {
        if (initialized) {
            clientNetwork.mainThreadAction();
        }
    }

    /**
     * Stage ID 설정
     *
     * @param stageId Stage ID
     */
    public void setStageId(long stageId) {
        this.stageId = stageId;
    }

    /**
     * 현재 Stage ID 반환
     *
     * @return Stage ID
     */
    public long getStageId() {
        return stageId;
    }

    // ===== 콜백 설정 =====

    /**
     * 연결 콜백 설정
     *
     * @param callback 연결 결과 콜백 (성공 여부)
     */
    public void setOnConnect(Runnable callback) {
        checkInitialized();
        clientNetwork.setOnConnect(success -> callback.run());
    }

    /**
     * 메시지 수신 콜백 설정
     *
     * @param callback 수신 콜백
     */
    public void setOnReceive(Consumer<Packet> callback) {
        checkInitialized();
        clientNetwork.setOnReceive(callback);
    }

    /**
     * 에러 콜백 설정
     *
     * @param callback 에러 콜백 (에러 코드, 메시지)
     */
    public void setOnError(BiConsumer<Integer, String> callback) {
        checkInitialized();
        clientNetwork.setOnError(callback);
    }

    /**
     * 연결 해제 콜백 설정
     *
     * @param callback 연결 해제 콜백
     */
    public void setOnDisconnect(Runnable callback) {
        checkInitialized();
        clientNetwork.setOnDisconnect(callback);
    }

    // ===== AutoCloseable =====

    /**
     * 리소스 정리
     */
    @Override
    public void close() {
        if (initialized) {
            logger.info("Closing connector");
            disconnect();
            clientNetwork.shutdown();
            initialized = false;
        }
    }

    // ===== Private Methods =====

    private void checkInitialized() {
        if (!initialized) {
            throw new IllegalStateException("Connector not initialized. Call init() first.");
        }
    }

    private void checkConnected() {
        checkInitialized();
        if (!isConnected()) {
            throw new IllegalStateException("Not connected to server");
        }
    }

    /**
     * 현재 설정 반환
     *
     * @return ConnectorConfig
     */
    public ConnectorConfig getConfig() {
        return config;
    }
}
