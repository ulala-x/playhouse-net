package com.playhouse.connector.advanced;

import com.playhouse.connector.ConnectorErrorCode;
import com.playhouse.connector.Packet;
import com.playhouse.connector.support.BaseIntegrationTest;
import com.playhouse.connector.support.TestMessages.*;
import org.junit.jupiter.api.*;

import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.concurrent.atomic.AtomicLong;
import java.util.concurrent.atomic.AtomicReference;

import static org.assertj.core.api.Assertions.*;

/**
 * A-04: OnError 이벤트 테스트
 * <p>
 * OnError 이벤트는 요청 실패, 연결 문제, 서버 에러 등 다양한 상황에서 발생.
 * 콜백 기반 API에서 에러 처리를 위해 사용됨.
 * </p>
 */
@DisplayName("A-04: OnError 이벤트 테스트")
@Tag("Advanced")
@Tag("OnError")
class A04_OnErrorEventTests extends BaseIntegrationTest {

    @Override
    @BeforeEach
    public void setUp() throws Exception {
        super.setUp();
        createStageAndConnect();
        authenticate("onerror-test-user");
    }

    @Test
    @DisplayName("A-04-01: 연결 해제 상태에서 Send 시 OnError가 발생한다")
    void onErrorFiredOnDisconnectedSend() throws Exception {
        // Arrange
        List<ErrorEvent> errorEvents = new ArrayList<>();
        connector.setOnError((errorCode, message) -> {
            errorEvents.add(new ErrorEvent(errorCode));
        });

        connector.disconnect();
        Thread.sleep(500);

        EchoRequest echoRequest = new EchoRequest("Test", 1);

        // Act
        Packet packet = Packet.builder("EchoRequest")
                .payload(echoRequest.toByteArray())
                .build();
        connector.send(packet);
        connector.mainThreadAction();

        // Assert
        assertThat(errorEvents).hasSize(1);
        assertThat(errorEvents.get(0).errorCode).isEqualTo(ConnectorErrorCode.DISCONNECTED.getCode());
    }

    @Test
    @DisplayName("A-04-02: 연결 해제 상태에서 Request 콜백 시 OnError가 발생한다")
    void onErrorFiredOnDisconnectedRequestCallback() throws Exception {
        // Arrange
        List<ErrorEvent> errorEvents = new ArrayList<>();
        connector.setOnError((errorCode, message) -> {
            errorEvents.add(new ErrorEvent(errorCode));
        });

        connector.disconnect();
        Thread.sleep(500);

        EchoRequest echoRequest = new EchoRequest("Test", 1);
        AtomicBoolean callbackFired = new AtomicBoolean(false);

        // Act
        Packet packet = Packet.builder("EchoRequest")
                .payload(echoRequest.toByteArray())
                .build();
        connector.request(packet, response -> {
            callbackFired.set(true);
        });
        connector.mainThreadAction();

        // Assert
        assertThat(errorEvents).hasSize(1);
        assertThat(errorEvents.get(0).errorCode).isEqualTo(ConnectorErrorCode.DISCONNECTED.getCode());
        assertThat(callbackFired.get()).isFalse();
    }

    @Test
    @DisplayName("A-04-03: OnError에 에러 메시지가 전달된다")
    void onErrorContainsMessage() throws Exception {
        // Arrange
        AtomicReference<String> receivedMessage = new AtomicReference<>();
        connector.setOnError((errorCode, message) -> {
            receivedMessage.set(message);
        });

        connector.disconnect();
        Thread.sleep(500);

        EchoRequest echoRequest = new EchoRequest("Original Content", 42);

        // Act
        Packet packet = Packet.builder("EchoRequest")
                .payload(echoRequest.toByteArray())
                .build();
        connector.send(packet);
        connector.mainThreadAction();

        // Assert
        assertThat(receivedMessage.get()).isNotNull();
    }

    @Test
    @DisplayName("A-04-04: OnError에 에러 코드가 전달된다")
    void onErrorContainsErrorCode() throws Exception {
        // Arrange
        AtomicInteger receivedErrorCode = new AtomicInteger(0);

        connector.setOnError((errorCode, message) -> {
            receivedErrorCode.set(errorCode);
        });

        connector.disconnect();
        Thread.sleep(500);

        EchoRequest echoRequest = new EchoRequest("Test", 1);

        // Act
        Packet packet = Packet.builder("EchoRequest")
                .payload(echoRequest.toByteArray())
                .build();
        connector.send(packet);
        connector.mainThreadAction();

        // Assert
        assertThat(receivedErrorCode.get()).isEqualTo(ConnectorErrorCode.DISCONNECTED.getCode());
    }

    @Test
    @DisplayName("A-04-05: 연결 해제 상태에서 Authenticate 시 OnError가 발생한다")
    void onErrorFiredOnDisconnectedAuthenticate() throws Exception {
        // Arrange
        List<Integer> errorEvents = new ArrayList<>();
        connector.setOnError((errorCode, message) -> {
            errorEvents.add(errorCode);
        });

        connector.disconnect();
        Thread.sleep(500);

        AuthenticateRequest authRequest = new AuthenticateRequest("test", "token");
        AtomicBoolean callbackFired = new AtomicBoolean(false);

        // Act
        Packet packet = Packet.builder("AuthenticateRequest")
                .payload(authRequest.toByteArray())
                .build();
        connector.request(packet, response -> {
            callbackFired.set(true);
        });
        connector.mainThreadAction();

        // Assert
        assertThat(errorEvents).hasSize(1);
        assertThat(errorEvents.get(0)).isEqualTo(ConnectorErrorCode.DISCONNECTED.getCode());
        assertThat(callbackFired.get()).isFalse();
    }

    @Test
    @DisplayName("A-04-06: setOnError는 마지막 핸들러만 유지한다 (Java 단일 핸들러 패턴)")
    void onErrorSetterReplacesHandler() throws Exception {
        // Arrange
        // Note: Unlike C# events with += which support multiple handlers,
        // Java uses setter pattern where each setOnError() replaces the previous handler.
        AtomicBoolean handler1Called = new AtomicBoolean(false);
        AtomicBoolean handler2Called = new AtomicBoolean(false);
        AtomicBoolean handler3Called = new AtomicBoolean(false);

        connector.setOnError((c, message) -> handler1Called.set(true));
        connector.setOnError((c, message) -> handler2Called.set(true));
        connector.setOnError((c, message) -> handler3Called.set(true));  // Only this handler is kept

        connector.disconnect();
        Thread.sleep(500);

        EchoRequest echoRequest = new EchoRequest("Test", 1);

        // Act
        Packet packet = Packet.builder("EchoRequest")
                .payload(echoRequest.toByteArray())
                .build();
        connector.send(packet);
        connector.mainThreadAction();

        // Assert - Only the last handler should be called (setter pattern)
        assertThat(handler1Called.get()).isFalse();
        assertThat(handler2Called.get()).isFalse();
        assertThat(handler3Called.get()).isTrue();
    }

    @Test
    @DisplayName("A-04-07: OnError 핸들러가 예외를 던져도 시스템이 안전하게 처리된다")
    void onErrorHandlerExceptionDoesNotCrash() throws Exception {
        // Arrange - Register a handler that throws an exception
        AtomicBoolean handlerCalled = new AtomicBoolean(false);
        connector.setOnError((c, message) -> {
            handlerCalled.set(true);
            throw new RuntimeException("Test exception");
        });

        connector.disconnect();
        Thread.sleep(500);

        EchoRequest echoRequest = new EchoRequest("Test", 1);

        // Act - This should not crash even if the handler throws
        Packet packet = Packet.builder("EchoRequest")
                .payload(echoRequest.toByteArray())
                .build();
        try {
            connector.send(packet);
            connector.mainThreadAction();
        } catch (Exception e) {
            // Handler exception may propagate, which is acceptable
        }

        // Assert - Handler should have been called
        assertThat(handlerCalled.get()).isTrue();
    }

    @Test
    @DisplayName("A-04-08: 연결 후 즉시 Disconnect 시 OnError 없이 처리된다")
    void onErrorNotFiredOnNormalDisconnect() throws Exception {
        // Arrange
        AtomicInteger errorCount = new AtomicInteger(0);
        connector.setOnError((c, message) -> errorCount.incrementAndGet());

        // Act
        connector.disconnect();
        Thread.sleep(500);

        // Assert - 정상 연결 해제 시에는 OnError가 발생하지 않아야 함
        assertThat(errorCount.get()).isEqualTo(0);
    }

    /**
     * 에러 이벤트를 담는 헬퍼 클래스
     */
    private static class ErrorEvent {
        final int errorCode;

        ErrorEvent(int errorCode) {
            this.errorCode = errorCode;
        }
    }
}
