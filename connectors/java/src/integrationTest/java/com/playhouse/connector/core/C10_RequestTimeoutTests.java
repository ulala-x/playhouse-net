package com.playhouse.connector.core;

import com.playhouse.connector.Connector;
import com.playhouse.connector.ConnectorConfig;
import com.playhouse.connector.ConnectorErrorCode;
import com.playhouse.connector.ConnectorException;
import com.playhouse.connector.Packet;
import com.playhouse.connector.support.BaseIntegrationTest;
import com.playhouse.connector.support.TestMessages;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;

import java.util.concurrent.CompletableFuture;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicInteger;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

/**
 * C-10: 요청 타임아웃 테스트
 * <p>
 * 서버가 응답하지 않는 경우 요청이 타임아웃되는지 검증합니다.
 * NoResponseRequest를 보내면 서버가 의도적으로 응답하지 않습니다.
 * </p>
 */
@DisplayName("C-10: Request Timeout Tests")
public class C10_RequestTimeoutTests extends BaseIntegrationTest {

    @Test
    @DisplayName("C-10-01: 응답이 없는 요청은 타임아웃된다")
    public void requestAsync_withNoResponse_timesOut() throws Exception {
        // Given: 짧은 타임아웃 설정 (2초)
        connector.close();
        connector = new Connector();
        connector.init(ConnectorConfig.builder()
                .requestTimeoutMs(2000) // 2초 타임아웃
                .heartbeatIntervalMs(10000)
                .build());

        createStageAndConnect();
        authenticate("timeoutUser");

        TestMessages.NoResponseRequest noResponseRequest = new TestMessages.NoResponseRequest(10000); // 10초 동안 응답 안 함
        Packet requestPacket = Packet.builder("NoResponseRequest")
                .payload(noResponseRequest.toByteArray())
                .build();

        // When & Then: 응답이 없는 요청은 타임아웃 예외 발생
        assertThatThrownBy(() -> connector.requestAsync(requestPacket).get(5, TimeUnit.SECONDS))
                .as("타임아웃이 발생해야 함")
                .hasCauseInstanceOf(ConnectorException.class)
                .matches(ex -> {
                    ConnectorException cause = (ConnectorException) ex.getCause();
                    return cause.getErrorCode() == ConnectorErrorCode.REQUEST_TIMEOUT.getCode();
                }, "에러 코드가 Timeout이어야 함");
    }

    @Test
    @DisplayName("C-10-02: 타임아웃 후에도 연결은 유지된다")
    public void connection_afterTimeout_remainsConnected() throws Exception {
        // Given: 짧은 타임아웃 설정
        connector.close();
        connector = new Connector();
        connector.init(ConnectorConfig.builder()
                .requestTimeoutMs(2000)
                .heartbeatIntervalMs(10000)
                .build());

        createStageAndConnect();
        authenticate("timeoutConnectionUser");

        TestMessages.NoResponseRequest noResponseRequest = new TestMessages.NoResponseRequest(10000);
        Packet requestPacket = Packet.builder("NoResponseRequest")
                .payload(noResponseRequest.toByteArray())
                .build();

        // When: 타임아웃 발생
        try {
            connector.requestAsync(requestPacket).get(5, TimeUnit.SECONDS);
        } catch (Exception ex) {
            // 타임아웃 예외 무시
        }

        // Then: 연결은 유지되어야 함
        assertThat(connector.isConnected())
                .as("타임아웃 후에도 연결은 유지되어야 함")
                .isTrue();
        assertThat(connector.isAuthenticated())
                .as("인증 상태도 유지되어야 함")
                .isTrue();

        // 다른 요청은 정상 동작해야 함
        TestMessages.EchoReply echoReply = echo("After Timeout", 1);
        assertThat(echoReply.content)
                .as("타임아웃 후 다른 요청은 정상 동작해야 함")
                .isEqualTo("After Timeout");
    }

    @Test
    @DisplayName("C-10-03: 콜백 방식 Request도 타임아웃된다")
    public void request_withCallback_timesOut() throws Exception {
        // Given: 짧은 타임아웃 설정
        connector.close();
        connector = new Connector();
        connector.init(ConnectorConfig.builder()
                .requestTimeoutMs(2000)
                .heartbeatIntervalMs(10000)
                .build());

        createStageAndConnect();
        authenticate("callbackTimeoutUser");

        TestMessages.NoResponseRequest noResponseRequest = new TestMessages.NoResponseRequest(10000);

        boolean[] callbackInvoked = {false};
        AtomicInteger errorCode = new AtomicInteger();
        CompletableFuture<Boolean> errorFuture = new CompletableFuture<>();

        connector.setOnError((code, message) -> {
            errorCode.set(code);
            errorFuture.complete(true);
        });

        // When: 콜백 방식으로 응답 없는 요청
        Packet requestPacket = Packet.builder("NoResponseRequest")
                .payload(noResponseRequest.toByteArray())
                .build();
        connector.request(requestPacket, response -> {
            callbackInvoked[0] = true;
        });

        // OnError 이벤트 대기 (MainThreadAction 호출하면서 최대 5초)
        boolean completed = waitForCondition(() -> errorFuture.isDone(), 5000);

        // Then: OnError 이벤트가 발생하고 콜백은 호출되지 않아야 함
        assertThat(completed)
                .as("OnError 이벤트가 발생해야 함")
                .isTrue();
        assertThat(errorCode.get())
                .as("에러 코드가 Timeout이어야 함")
                .isEqualTo(ConnectorErrorCode.REQUEST_TIMEOUT.getCode());
        assertThat(callbackInvoked[0])
                .as("타임아웃 시 성공 콜백은 호출되지 않아야 함")
                .isFalse();
    }

    @Test
    @DisplayName("C-10-04: 여러 요청 중 하나만 타임아웃되어도 다른 요청은 정상 처리된다")
    public void multipleRequests_oneTimesOut_othersSucceed() throws Exception {
        // Given: 짧은 타임아웃 설정
        connector.close();
        connector = new Connector();
        connector.init(ConnectorConfig.builder()
                .requestTimeoutMs(2000)
                .heartbeatIntervalMs(10000)
                .build());

        createStageAndConnect();
        authenticate("multiTimeoutUser");

        // When: 정상 요청과 타임아웃 요청을 병렬로 전송
        CompletableFuture<TestMessages.EchoReply> echoFuture1 = CompletableFuture.supplyAsync(() -> {
            try {
                return echo("Normal 1", 1);
            } catch (Exception e) {
                throw new RuntimeException(e);
            }
        });

        CompletableFuture<TestMessages.EchoReply> echoFuture2 = CompletableFuture.supplyAsync(() -> {
            try {
                return echo("Normal 2", 2);
            } catch (Exception e) {
                throw new RuntimeException(e);
            }
        });

        TestMessages.NoResponseRequest noResponseRequest = new TestMessages.NoResponseRequest(10000);
        Packet timeoutPacket = Packet.builder("NoResponseRequest")
                .payload(noResponseRequest.toByteArray())
                .build();
        CompletableFuture<Packet> timeoutFuture = connector.requestAsync(timeoutPacket);

        CompletableFuture<TestMessages.EchoReply> echoFuture3 = CompletableFuture.supplyAsync(() -> {
            try {
                Thread.sleep(100);
                return echo("Normal 3", 3);
            } catch (Exception e) {
                throw new RuntimeException(e);
            }
        });

        // 정상 요청들 완료 대기
        TestMessages.EchoReply echo1 = echoFuture1.get(5, TimeUnit.SECONDS);
        TestMessages.EchoReply echo2 = echoFuture2.get(5, TimeUnit.SECONDS);
        TestMessages.EchoReply echo3 = echoFuture3.get(5, TimeUnit.SECONDS);

        // Then: 정상 요청들은 성공해야 함
        assertThat(echo1.content).isEqualTo("Normal 1");
        assertThat(echo2.content).isEqualTo("Normal 2");
        assertThat(echo3.content).isEqualTo("Normal 3");

        // 타임아웃 요청은 예외가 발생해야 함
        assertThatThrownBy(() -> timeoutFuture.get(5, TimeUnit.SECONDS))
                .hasCauseInstanceOf(ConnectorException.class);
    }

    @Test
    @DisplayName("C-10-05: 타임아웃 시간을 길게 설정하면 응답을 받을 수 있다")
    public void requestAsync_withLongTimeout_receivesResponse() throws Exception {
        // Given: 긴 타임아웃 설정 (15초)
        connector.close();
        connector = new Connector();
        connector.init(ConnectorConfig.builder()
                .requestTimeoutMs(15000) // 15초 타임아웃
                .heartbeatIntervalMs(10000)
                .build());

        createStageAndConnect();
        authenticate("longTimeoutUser");

        // When: 짧은 지연으로 Echo 요청 (타임아웃 내에 응답)
        TestMessages.EchoReply echoReply = echo("Long Timeout Test", 1);

        // Then: 정상적으로 응답을 받아야 함
        assertThat(echoReply.content)
                .as("긴 타임아웃 설정으로 응답을 받아야 함")
                .isEqualTo("Long Timeout Test");
    }

    @Test
    @DisplayName("C-10-06: 인증 요청도 타임아웃될 수 있다")
    public void authenticateAsync_withTimeout_throwsException() throws Exception {
        // Given: 매우 짧은 타임아웃 설정 (100ms)
        connector.close();
        connector = new Connector();
        connector.init(ConnectorConfig.builder()
                .requestTimeoutMs(100) // 100ms 타임아웃
                .heartbeatIntervalMs(10000)
                .build());

        createStageAndConnect();

        // When: 인증 요청 (네트워크 지연이 있으면 타임아웃될 수 있음)
        TestMessages.AuthenticateRequest authRequest = new TestMessages.AuthenticateRequest("authTimeoutUser", "valid_token");
        Packet requestPacket = Packet.builder("AuthenticateRequest")
                .payload(authRequest.toByteArray())
                .build();

        // Then: 예외가 발생하면 ConnectorException이어야 함
        try {
            connector.requestAsync(requestPacket).get(5, TimeUnit.SECONDS);
        } catch (Exception ex) {
            // 예외가 발생하면 ConnectorException이어야 함
            if (ex.getCause() != null) {
                assertThat(ex.getCause())
                        .as("타임아웃 시 ConnectorException이 발생해야 함")
                        .isInstanceOf(ConnectorException.class);
            }
        }
    }

    @Test
    @DisplayName("C-10-07: 타임아웃된 요청의 정보를 확인할 수 있다")
    public void timeoutException_containsRequestInfo() throws Exception {
        // Given: 짧은 타임아웃 설정
        connector.close();
        connector = new Connector();
        connector.init(ConnectorConfig.builder()
                .requestTimeoutMs(2000)
                .heartbeatIntervalMs(10000)
                .build());

        createStageAndConnect();
        authenticate("exceptionInfoUser");

        TestMessages.NoResponseRequest noResponseRequest = new TestMessages.NoResponseRequest(10000);
        Packet requestPacket = Packet.builder("NoResponseRequest")
                .payload(noResponseRequest.toByteArray())
                .build();

        // When: 타임아웃 발생
        ConnectorException caughtException = null;
        try {
            connector.requestAsync(requestPacket).get(5, TimeUnit.SECONDS);
        } catch (Exception ex) {
            if (ex.getCause() instanceof ConnectorException) {
                caughtException = (ConnectorException) ex.getCause();
            }
        }

        // Then: 예외에 요청 정보가 포함되어야 함
        assertThat(caughtException)
                .as("타임아웃 예외가 발생해야 함")
                .isNotNull();
        assertThat(caughtException.getErrorCode())
                .isEqualTo(ConnectorErrorCode.REQUEST_TIMEOUT.getCode());
        // 예외 메시지에 타임아웃 관련 정보가 포함되어야 함
        assertThat(caughtException.getMessage())
                .as("예외 메시지에 정보가 포함되어야 함")
                .isNotNull();
    }
}
