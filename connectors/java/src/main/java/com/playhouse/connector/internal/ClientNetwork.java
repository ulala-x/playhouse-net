package com.playhouse.connector.internal;

import com.playhouse.connector.ConnectorConfig;
import com.playhouse.connector.ConnectorException;
import com.playhouse.connector.Packet;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.nio.ByteBuffer;
import java.util.concurrent.*;
import java.util.function.BiConsumer;
import java.util.function.Consumer;

/**
 * 클라이언트 네트워킹 코어
 * <p>
 * TCP 연결, 패킷 송수신, 요청-응답 매칭 관리
 */
public final class ClientNetwork {

    private static final Logger logger = LoggerFactory.getLogger(ClientNetwork.class);

    private final ConnectorConfig config;
    private final IConnection connection;
    private final RequestCache requestCache;
    private final BlockingQueue<Runnable> callbackQueue;

    // Callbacks - volatile for thread safety (callbacks set from main thread, read from Netty threads)
    private volatile Consumer<Boolean> onConnect;
    private volatile Consumer<Packet> onReceive;
    private volatile BiConsumer<Integer, String> onError;
    private volatile Runnable onDisconnect;

    private volatile boolean authenticated;

    /**
     * ClientNetwork 생성자
     *
     * @param config Connector 설정
     */
    public ClientNetwork(ConnectorConfig config) {
        this.config = config;

        // TCP 또는 WebSocket 연결 선택
        if (config.isUseWebsocket()) {
            logger.info("Using WebSocket connection");
            this.connection = new WsConnection(config);
        } else {
            logger.info("Using TCP connection");
            this.connection = new TcpConnection(config);
        }

        this.requestCache = new RequestCache(config.getRequestTimeoutMs());
        this.callbackQueue = new LinkedBlockingQueue<>();
        this.authenticated = false;
    }

    /**
     * 연결 콜백 설정
     */
    public void setOnConnect(Consumer<Boolean> onConnect) {
        this.onConnect = onConnect;
    }

    /**
     * 수신 콜백 설정
     */
    public void setOnReceive(Consumer<Packet> onReceive) {
        this.onReceive = onReceive;
    }

    /**
     * 에러 콜백 설정
     */
    public void setOnError(BiConsumer<Integer, String> onError) {
        this.onError = onError;
    }

    /**
     * 연결 해제 콜백 설정
     */
    public void setOnDisconnect(Runnable onDisconnect) {
        this.onDisconnect = onDisconnect;
    }

    /**
     * 서버에 비동기 연결
     *
     * @param host 서버 호스트
     * @param port 서버 포트
     * @return CompletableFuture<Boolean>
     */
    public CompletableFuture<Boolean> connectAsync(String host, int port) {
        boolean useSsl = config.isUseSsl();
        int timeoutMs = config.getConnectionIdleTimeoutMs();

        return connection.connectAsync(host, port, useSsl, timeoutMs)
            .thenApply(success -> {
                if (success) {
                    logger.info("Connection established (SSL: {}, WebSocket: {})",
                        useSsl, config.isUseWebsocket());
                    connection.startReceive(this::handleReceive, this::handleDisconnect);
                    enqueueCallback(() -> {
                        if (onConnect != null) {
                            onConnect.accept(true);
                        }
                    });
                } else {
                    logger.error("Connection failed");
                    enqueueCallback(() -> {
                        if (onConnect != null) {
                            onConnect.accept(false);
                        }
                    });
                }
                return success;
            });
    }

    /**
     * 연결 상태 확인
     */
    public boolean isConnected() {
        return connection.isConnected();
    }

    /**
     * 인증 상태 확인
     */
    public boolean isAuthenticated() {
        return authenticated;
    }

    /**
     * 인증 상태 설정
     */
    public void setAuthenticated(boolean authenticated) {
        this.authenticated = authenticated;
    }

    /**
     * 메시지 전송 (응답 없음)
     *
     * @param packet  패킷
     * @param stageId Stage ID
     */
    public void send(Packet packet, long stageId) {
        if (!connection.isConnected()) {
            logger.warn("Cannot send: not connected");
            return;
        }

        ByteBuffer buffer = PacketCodec.encodeRequest(packet, (short) 0, stageId);
        connection.sendAsync(buffer)
            .thenAccept(success -> {
                if (!success) {
                    logger.error("Failed to send packet: {}", packet.getMsgId());
                    enqueueCallback(() -> {
                        if (onError != null) {
                            onError.accept(1000, "Send failed");
                        }
                    });
                }
            });
    }

    /**
     * 요청 전송 (비동기 응답)
     *
     * @param packet  요청 패킷
     * @param stageId Stage ID
     * @return CompletableFuture<Packet>
     */
    public CompletableFuture<Packet> requestAsync(Packet packet, long stageId) {
        if (!connection.isConnected()) {
            return CompletableFuture.failedFuture(
                new ConnectorException(1001, "Not connected", packet)
            );
        }

        // Use registerWithSeq to get msgSeq directly (avoids O(n) scan and race conditions)
        RequestCache.RegisterResult result = requestCache.registerWithSeq(packet);
        short msgSeq = result.msgSeq;
        CompletableFuture<Packet> responseFuture = result.future;

        ByteBuffer buffer = PacketCodec.encodeRequest(packet, msgSeq, stageId);
        connection.sendAsync(buffer)
            .thenAccept(success -> {
                if (!success) {
                    logger.error("Failed to send request: {}", packet.getMsgId());
                    // Remove the entry from cache to prevent orphaned requests
                    requestCache.remove(msgSeq);
                    responseFuture.completeExceptionally(
                        new ConnectorException(1000, "Send failed", packet)
                    );
                }
            });

        return responseFuture;
    }

    /**
     * 연결 종료
     */
    public CompletableFuture<Void> disconnectAsync() {
        logger.info("Disconnecting...");
        authenticated = false;
        requestCache.cancelAll();
        return connection.disconnectAsync();
    }

    /**
     * 메인 스레드에서 콜백 실행
     */
    public void mainThreadAction() {
        Runnable callback;
        while ((callback = callbackQueue.poll()) != null) {
            try {
                callback.run();
            } catch (Exception e) {
                logger.error("Error executing callback", e);
            }
        }
    }

    /**
     * 수신 처리
     */
    private void handleReceive(ByteBuffer buffer) {
        while (buffer.remaining() > 0) {
            // 패킷 크기 확인
            int packetSize = PacketCodec.peekPacketSize(buffer);
            if (packetSize == -1 || buffer.remaining() < packetSize) {
                // 데이터 부족, 다음 수신 대기
                break;
            }

            if (packetSize == -2) {
                // 유효하지 않은 패킷 크기 (보안 위반 또는 프로토콜 오류)
                logger.error("Invalid packet size detected, closing connection");
                enqueueCallback(() -> {
                    if (onError != null) {
                        onError.accept(2002, "Invalid packet size");
                    }
                });
                // 연결 종료
                disconnectAsync();
                return;
            }

            // 패킷 추출 (ContentSize 포함하여 전체 패킷을 slice)
            int startPos = buffer.position();
            int endPos = startPos + packetSize;
            ByteBuffer packetBuffer = buffer.slice().limit(packetSize);
            buffer.position(endPos);

            // 패킷 디코딩
            try {
                Packet packet = PacketCodec.decodeResponse(packetBuffer);

                // 요청-응답 매칭
                if (!requestCache.complete(packet)) {
                    // Push 메시지
                    enqueueCallback(() -> {
                        if (onReceive != null) {
                            onReceive.accept(packet);
                        }
                    });
                }

            } catch (Exception e) {
                logger.error("Failed to decode packet", e);
                enqueueCallback(() -> {
                    if (onError != null) {
                        onError.accept(2001, "Decode failed: " + e.getMessage());
                    }
                });
            }
        }
    }

    /**
     * 연결 종료 처리
     */
    private void handleDisconnect() {
        logger.info("Connection closed");
        authenticated = false;
        requestCache.cancelAll();
        enqueueCallback(() -> {
            if (onDisconnect != null) {
                onDisconnect.run();
            }
        });
    }

    /**
     * 콜백 큐에 추가
     */
    private void enqueueCallback(Runnable callback) {
        if (callback == null) {
            logger.warn("Null callback provided, ignoring");
            return;
        }

        if (!callbackQueue.offer(callback)) {
            logger.error("Callback queue full (size: {}), dropping callback. Consider increasing queue size or processing callbacks more frequently.",
                callbackQueue.size());
            // 큐가 가득 찬 경우 즉시 실행하는 것은 위험할 수 있음 (스택 오버플로우 가능성)
            // 대신 에러 로그만 남기고 드롭
        }
    }

    /**
     * 리소스 정리
     */
    public void shutdown() {
        logger.info("Shutting down ClientNetwork");
        requestCache.shutdown();
        connection.disconnectAsync().join();
        connection.shutdown(); // Netty EventLoopGroup graceful shutdown
    }
}
