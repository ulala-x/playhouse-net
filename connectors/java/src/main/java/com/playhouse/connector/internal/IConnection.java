package com.playhouse.connector.internal;

import java.nio.ByteBuffer;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.TimeUnit;
import java.util.function.Consumer;

/**
 * 네트워크 연결 추상 인터페이스
 * <p>
 * TCP 및 WebSocket 연결을 추상화하여 동일한 인터페이스로 사용할 수 있습니다.
 */
public interface IConnection {

    /**
     * 서버에 비동기 연결
     *
     * @param host      서버 호스트
     * @param port      서버 포트
     * @param useSsl    SSL/TLS 사용 여부
     * @param timeoutMs 연결 타임아웃 (밀리초)
     * @return 연결 성공 여부를 반환하는 CompletableFuture
     */
    CompletableFuture<Boolean> connectAsync(String host, int port, boolean useSsl, int timeoutMs);

    /**
     * 연결 상태 확인
     *
     * @return 연결되어 있으면 true
     */
    boolean isConnected();

    /**
     * 데이터 전송
     *
     * @param data 전송할 데이터
     * @return 전송 성공 여부를 반환하는 CompletableFuture
     */
    CompletableFuture<Boolean> sendAsync(ByteBuffer data);

    /**
     * 데이터 수신 시작 (비동기 루프)
     *
     * @param onReceive 데이터 수신 콜백
     * @param onClosed  연결 종료 콜백
     */
    void startReceive(Consumer<ByteBuffer> onReceive, Runnable onClosed);

    /**
     * 연결 종료
     *
     * @return CompletableFuture<Void>
     */
    CompletableFuture<Void> disconnectAsync();

    /**
     * 리소스 정리 (EventLoopGroup 등)
     */
    void shutdown();

    /**
     * EventLoopGroup 종료 대기
     *
     * @param timeout  대기 시간
     * @param timeUnit 시간 단위
     * @return 정상 종료되면 true, 타임아웃이면 false
     * @throws InterruptedException 대기 중 인터럽트 발생
     */
    boolean awaitTermination(long timeout, TimeUnit timeUnit) throws InterruptedException;
}
