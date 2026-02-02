package com.playhouse.connector.internal;

import com.playhouse.connector.ConnectorErrorCode;
import com.playhouse.connector.ConnectorException;
import com.playhouse.connector.Packet;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.util.concurrent.CompletableFuture;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicInteger;

/**
 * 요청-응답 매칭 관리
 * <p>
 * MsgSeq를 사용하여 요청과 응답을 매칭합니다.
 * 타임아웃된 요청은 자동으로 제거됩니다.
 */
public final class RequestCache {

    private static final Logger logger = LoggerFactory.getLogger(RequestCache.class);

    private final ConcurrentHashMap<Short, RequestEntry> requests;
    private final AtomicInteger seqGenerator;
    private final int requestTimeoutMs;
    private final ScheduledExecutorService timeoutExecutor;

    /**
     * RequestCache 생성자
     *
     * @param requestTimeoutMs 요청 타임아웃 (밀리초)
     */
    public RequestCache(int requestTimeoutMs) {
        this.requests = new ConcurrentHashMap<>();
        this.seqGenerator = new AtomicInteger(1); // 0은 Push 용도로 예약
        this.requestTimeoutMs = requestTimeoutMs;
        this.timeoutExecutor = Executors.newSingleThreadScheduledExecutor(r -> {
            Thread thread = new Thread(r, "RequestCache-Timeout");
            thread.setDaemon(true);
            return thread;
        });
    }

    /**
     * 새로운 요청 등록
     *
     * @param packet 요청 패킷
     * @return CompletableFuture<Packet>
     * @deprecated Use {@link #registerWithSeq(Packet)} instead to get msgSeq directly and avoid race conditions
     */
    @Deprecated
    public CompletableFuture<Packet> register(Packet packet) {
        return registerWithSeq(packet).future;
    }

    /**
     * 새로운 요청 등록 (msgSeq와 future 함께 반환)
     *
     * @param packet 요청 패킷
     * @return RegisterResult containing msgSeq and CompletableFuture
     */
    public RegisterResult registerWithSeq(Packet packet) {
        short msgSeq = generateMsgSeq();
        CompletableFuture<Packet> future = new CompletableFuture<>();
        RequestEntry entry = new RequestEntry(packet, future, System.currentTimeMillis());

        requests.put(msgSeq, entry);

        // 타임아웃 스케줄링
        timeoutExecutor.schedule(() -> {
            RequestEntry timeoutEntry = requests.remove(msgSeq);
            if (timeoutEntry != null && !timeoutEntry.future.isDone()) {
                logger.warn("Request timeout: msgId={}, msgSeq={}", packet.getMsgId(), msgSeq);
                timeoutEntry.future.completeExceptionally(
                    new ConnectorException(
                        ConnectorErrorCode.REQUEST_TIMEOUT.getCode(),
                        String.format("Request timeout after %dms: msgId=%s, msgSeq=%d",
                            requestTimeoutMs, packet.getMsgId(), msgSeq),
                        timeoutEntry.packet
                    )
                );
            }
        }, requestTimeoutMs, TimeUnit.MILLISECONDS);

        if (logger.isDebugEnabled()) {
            logger.debug("Registered request: msgId={}, msgSeq={}", packet.getMsgId(), msgSeq);
        }

        return new RegisterResult(msgSeq, future);
    }

    /**
     * Result of registering a request containing msgSeq and the response future.
     */
    public static final class RegisterResult {
        public final short msgSeq;
        public final CompletableFuture<Packet> future;

        RegisterResult(short msgSeq, CompletableFuture<Packet> future) {
            this.msgSeq = msgSeq;
            this.future = future;
        }
    }

    /**
     * 응답 처리
     *
     * @param response 응답 패킷
     * @return 처리 성공 여부
     */
    public boolean complete(Packet response) {
        short msgSeq = response.getMsgSeq();

        if (msgSeq == 0) {
            // Push 메시지 (요청 없음)
            logger.debug("Received push message: msgId={}", response.getMsgId());
            return false;
        }

        RequestEntry entry = requests.remove(msgSeq);
        if (entry == null) {
            logger.warn("No matching request found for response: msgId={}, msgSeq={}",
                response.getMsgId(), msgSeq);
            return false;
        }

        if (response.hasError()) {
            logger.warn("Response has error: msgId={}, msgSeq={}, errorCode={}",
                response.getMsgId(), msgSeq, response.getErrorCode());
            // 에러 응답은 예외로 처리
            entry.future.completeExceptionally(
                new ConnectorException(response.getErrorCode(),
                    String.format("Server error: msgId=%s, errorCode=%d",
                        response.getMsgId(), response.getErrorCode()),
                    response)
            );
        } else {
            entry.future.complete(response);
        }

        if (logger.isDebugEnabled()) {
            long elapsed = System.currentTimeMillis() - entry.timestamp;
            logger.debug("Completed request: msgId={}, msgSeq={}, elapsed={}ms",
                response.getMsgId(), msgSeq, elapsed);
        }

        return true;
    }

    /**
     * MsgSeq 얻기
     *
     * @param future CompletableFuture
     * @return MsgSeq (없으면 -1)
     * @deprecated Use {@link #registerWithSeq(Packet)} instead to avoid O(n) scan and race conditions
     */
    @Deprecated
    public short getMsgSeq(CompletableFuture<Packet> future) {
        for (var entry : requests.entrySet()) {
            if (entry.getValue().future == future) {
                return entry.getKey();
            }
        }
        return -1;
    }

    /**
     * 요청 항목 제거 (전송 실패 시 사용)
     *
     * @param msgSeq 메시지 시퀀스
     */
    public void remove(short msgSeq) {
        RequestEntry entry = requests.remove(msgSeq);
        if (entry != null && logger.isDebugEnabled()) {
            logger.debug("Removed request entry: msgId={}, msgSeq={}", entry.packet.getMsgId(), msgSeq);
        }
    }

    /**
     * 모든 대기 중인 요청 취소
     */
    public void cancelAll() {
        logger.info("Cancelling all pending requests: count={}", requests.size());

        for (var entry : requests.values()) {
            if (!entry.future.isDone()) {
                entry.future.completeExceptionally(
                    new IllegalStateException("Connection closed")
                );
            }
        }
        requests.clear();
    }

    /**
     * 리소스 정리
     */
    public void shutdown() {
        logger.info("Shutting down RequestCache");
        cancelAll();

        if (timeoutExecutor == null || timeoutExecutor.isShutdown()) {
            return;
        }

        timeoutExecutor.shutdown();
        try {
            if (!timeoutExecutor.awaitTermination(1, TimeUnit.SECONDS)) {
                logger.warn("Timeout executor did not terminate gracefully, forcing shutdown");
                timeoutExecutor.shutdownNow();

                // 강제 종료 후에도 종료되지 않으면 재확인
                if (!timeoutExecutor.awaitTermination(1, TimeUnit.SECONDS)) {
                    logger.error("Timeout executor did not terminate after forced shutdown");
                }
            }
        } catch (InterruptedException e) {
            logger.warn("Interrupted while waiting for timeout executor to terminate");
            timeoutExecutor.shutdownNow();
            Thread.currentThread().interrupt();
        }
    }

    /**
     * MsgSeq 생성 (1 ~ 32767 순환)
     */
    private short generateMsgSeq() {
        int seq = seqGenerator.getAndUpdate(current -> {
            int next = current + 1;
            // Short.MAX_VALUE (32767)까지만 사용
            return (next > Short.MAX_VALUE) ? 1 : next;
        });
        return (short) seq;
    }

    /**
     * 대기 중인 요청 개수
     */
    public int pendingCount() {
        return requests.size();
    }

    /**
     * 요청 엔트리
     */
    private static final class RequestEntry {
        final Packet packet;
        final CompletableFuture<Packet> future;
        final long timestamp;

        RequestEntry(Packet packet, CompletableFuture<Packet> future, long timestamp) {
            this.packet = packet;
            this.future = future;
            this.timestamp = timestamp;
        }
    }
}
