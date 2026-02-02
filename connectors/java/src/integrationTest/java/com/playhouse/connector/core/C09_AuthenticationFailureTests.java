package com.playhouse.connector.core;

import com.playhouse.connector.ConnectorErrorCode;
import com.playhouse.connector.ConnectorException;
import com.playhouse.connector.Packet;
import com.playhouse.connector.support.BaseIntegrationTest;
import com.playhouse.connector.support.TestMessages;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;

import java.util.concurrent.TimeUnit;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

/**
 * C-09: 인증 실패 테스트
 * <p>
 * 잘못된 토큰이나 유효하지 않은 인증 정보로 인증이 실패하는 경우를 검증합니다.
 * 인증 실패 시 서버는 에러 코드(AuthenticationFailed=5)를 반환하고,
 * 커넥터는 ConnectorException을 throw합니다.
 * </p>
 */
@DisplayName("C-09: Authentication Failure Tests")
public class C09_AuthenticationFailureTests extends BaseIntegrationTest {

    // 서버에서 정의된 AuthenticationFailed 에러 코드
    private static final int AUTHENTICATION_FAILED_ERROR_CODE = 5;

    @Test
    @DisplayName("C-09-01: 잘못된 토큰으로 인증하면 실패한다")
    public void authenticate_withInvalidToken_fails() throws Exception {
        // Given: 연결된 상태
        createStageAndConnect();

        TestMessages.AuthenticateRequest authRequest = new TestMessages.AuthenticateRequest("testUser", "invalid_token");
        Packet requestPacket = Packet.builder("AuthenticateRequest")
                .payload(authRequest.toByteArray())
                .build();

        // When & Then: 잘못된 토큰으로 인증 시도하면 예외 발생
        assertThatThrownBy(() -> connector.requestAsync(requestPacket).get(5, TimeUnit.SECONDS))
                .as("인증 실패 예외가 발생해야 함")
                .hasCauseInstanceOf(ConnectorException.class)
                .matches(ex -> {
                    ConnectorException cause = (ConnectorException) ex.getCause();
                    return cause.getErrorCode() == AUTHENTICATION_FAILED_ERROR_CODE;
                }, "인증 실패 에러 코드가 반환되어야 함");

        // IsAuthenticated도 false여야 함
        assertThat(connector.isAuthenticated())
                .as("인증 실패 시 IsAuthenticated는 false여야 함")
                .isFalse();
    }

    @Test
    @DisplayName("C-09-02: 빈 UserId로 인증하면 실패한다")
    public void authenticate_withEmptyUserId_fails() throws Exception {
        // Given: 연결된 상태
        createStageAndConnect();

        TestMessages.AuthenticateRequest authRequest = new TestMessages.AuthenticateRequest("", "valid_token");
        Packet requestPacket = Packet.builder("AuthenticateRequest")
                .payload(authRequest.toByteArray())
                .build();

        // When & Then: 빈 UserId로 인증 시도하면 예외 발생
        assertThatThrownBy(() -> connector.requestAsync(requestPacket).get(5, TimeUnit.SECONDS))
                .as("인증 실패 예외가 발생해야 함")
                .hasCauseInstanceOf(ConnectorException.class);

        assertThat(connector.isAuthenticated()).isFalse();
    }

    @Test
    @DisplayName("C-09-03: 빈 토큰으로 인증하면 실패한다")
    public void authenticate_withEmptyToken_fails() throws Exception {
        // Given: 연결된 상태
        createStageAndConnect();

        TestMessages.AuthenticateRequest authRequest = new TestMessages.AuthenticateRequest("testUser", "");
        Packet requestPacket = Packet.builder("AuthenticateRequest")
                .payload(authRequest.toByteArray())
                .build();

        // When & Then: 빈 토큰으로 인증 시도하면 예외 발생
        assertThatThrownBy(() -> connector.requestAsync(requestPacket).get(5, TimeUnit.SECONDS))
                .as("인증 실패 예외가 발생해야 함")
                .hasCauseInstanceOf(ConnectorException.class);

        assertThat(connector.isAuthenticated()).isFalse();
    }

    @Test
    @DisplayName("C-09-04: 인증 실패 후에도 연결은 유지된다")
    public void connection_afterAuthenticationFailure_remainsConnected() throws Exception {
        // Given: 연결된 상태
        createStageAndConnect();

        assertThat(connector.isConnected()).as("초기 연결 상태").isTrue();

        // When: 잘못된 토큰으로 인증 실패
        TestMessages.AuthenticateRequest authRequest = new TestMessages.AuthenticateRequest("testUser", "invalid_token");
        Packet requestPacket = Packet.builder("AuthenticateRequest")
                .payload(authRequest.toByteArray())
                .build();

        try {
            connector.requestAsync(requestPacket).get(5, TimeUnit.SECONDS);
        } catch (Exception ex) {
            // 예상된 예외 - 인증 실패
            assertThat(ex.getCause()).isInstanceOf(ConnectorException.class);
        }

        // Then: 인증은 실패했지만 연결은 유지되어야 함
        assertThat(connector.isConnected())
                .as("인증 실패 후에도 연결은 유지되어야 함")
                .isTrue();
        assertThat(connector.isAuthenticated())
                .as("인증은 실패 상태")
                .isFalse();
    }

    @Test
    @DisplayName("C-09-05: 인증 실패 후 재시도할 수 있다")
    public void authenticate_afterFailure_canRetry() throws Exception {
        // Given: 연결된 상태
        createStageAndConnect();

        // 첫 번째 인증 시도 (실패)
        TestMessages.AuthenticateRequest failRequest = new TestMessages.AuthenticateRequest("testUser", "invalid_token");
        Packet failPacket = Packet.builder("AuthenticateRequest")
                .payload(failRequest.toByteArray())
                .build();

        try {
            connector.requestAsync(failPacket).get(5, TimeUnit.SECONDS);
            org.junit.jupiter.api.Assertions.fail("첫 번째 인증은 예외가 발생해야 함");
        } catch (Exception ex) {
            assertThat(ex.getCause())
                    .as("첫 번째 인증은 실패해야 함")
                    .isInstanceOf(ConnectorException.class);
        }

        // When: 두 번째 인증 시도 (성공)
        TestMessages.AuthenticateReply authReply = authenticate("testUser", "valid_token");

        // Then: 두 번째 인증은 성공해야 함
        assertThat(authReply.success)
                .as("재시도한 인증은 성공해야 함")
                .isTrue();
        assertThat(connector.isAuthenticated())
                .as("인증 상태가 true여야 함")
                .isTrue();
    }

    @Test
    @DisplayName("C-09-06: 인증 실패 시 예외에 에러 정보가 포함된다")
    public void authenticationFailure_exception_containsErrorInfo() throws Exception {
        // Given: 연결된 상태
        createStageAndConnect();

        TestMessages.AuthenticateRequest authRequest = new TestMessages.AuthenticateRequest("failUser", "invalid_token");
        Packet requestPacket = Packet.builder("AuthenticateRequest")
                .payload(authRequest.toByteArray())
                .build();

        // When & Then: 인증 실패 시 예외에 에러 정보가 포함되어야 함
        assertThatThrownBy(() -> connector.requestAsync(requestPacket).get(5, TimeUnit.SECONDS))
                .hasCauseInstanceOf(ConnectorException.class)
                .matches(ex -> {
                    ConnectorException cause = (ConnectorException) ex.getCause();
                    return cause.getErrorCode() == AUTHENTICATION_FAILED_ERROR_CODE;
                }, "에러 코드가 AuthenticationFailed여야 함")
                .matches(ex -> {
                    ConnectorException cause = (ConnectorException) ex.getCause();
                    return cause.getMessage() != null;
                }, "에러 메시지가 있어야 함");
    }

    @Test
    @DisplayName("C-09-07: 인증 없이 메시지를 보낼 수 없다")
    public void sendMessage_withoutAuthentication_fails() throws Exception {
        // Given: 연결만 된 상태 (인증 안 함)
        createStageAndConnect();

        assertThat(connector.isConnected()).as("연결은 되어 있음").isTrue();
        assertThat(connector.isAuthenticated()).as("인증은 안 되어 있음").isFalse();

        // When: 인증 없이 Echo 요청 시도
        TestMessages.EchoRequest echoRequest = new TestMessages.EchoRequest("Test", 1);
        Packet requestPacket = Packet.builder("EchoRequest")
                .payload(echoRequest.toByteArray())
                .build();

        // Then: 예외가 발생하거나 에러 응답을 받아야 함
        assertThatThrownBy(() -> connector.requestAsync(requestPacket).get(5, TimeUnit.SECONDS))
                .as("인증 없이 메시지를 보내면 에러가 발생해야 함")
                .isNotNull();
    }

    @Test
    @DisplayName("C-09-08: 연결 없이 인증 시도하면 예외가 발생한다")
    public void authenticate_withoutConnection_throwsException() throws Exception {
        // Given: 연결되지 않은 상태
        assertThat(connector.isConnected()).as("초기 상태는 연결 안 됨").isFalse();

        TestMessages.AuthenticateRequest authRequest = new TestMessages.AuthenticateRequest("testUser", "valid_token");
        Packet requestPacket = Packet.builder("AuthenticateRequest")
                .payload(authRequest.toByteArray())
                .build();

        // When & Then: 연결 없이 인증 시도하면 예외 발생
        assertThatThrownBy(() -> connector.requestAsync(requestPacket).get(5, TimeUnit.SECONDS))
                .as("연결 없이 인증하면 예외가 발생해야 함")
                .hasCauseInstanceOf(ConnectorException.class)
                .matches(ex -> {
                    ConnectorException cause = (ConnectorException) ex.getCause();
                    return cause.getErrorCode() == ConnectorErrorCode.DISCONNECTED.getCode();
                }, "Disconnected 에러 코드여야 함");
    }

    @Test
    @DisplayName("C-09-09: 여러 번 인증 실패해도 연결은 유지된다")
    public void multipleAuthenticationFailures_connectionRemains() throws Exception {
        // Given: 연결된 상태
        createStageAndConnect();

        // When: 3번 연속 인증 실패
        for (int i = 1; i <= 3; i++) {
            TestMessages.AuthenticateRequest authRequest = new TestMessages.AuthenticateRequest(
                    "failUser" + i, "invalid_token");
            Packet requestPacket = Packet.builder("AuthenticateRequest")
                    .payload(authRequest.toByteArray())
                    .build();

            try {
                connector.requestAsync(requestPacket).get(5, TimeUnit.SECONDS);
                org.junit.jupiter.api.Assertions.fail(i + "번째 인증은 예외가 발생해야 함");
            } catch (Exception ex) {
                assertThat(ex.getCause())
                        .as(i + "번째 인증이 실패해야 함")
                        .isInstanceOf(ConnectorException.class);
            }
        }

        // Then: 여러 번 실패해도 연결은 유지되어야 함
        assertThat(connector.isConnected())
                .as("여러 번 인증 실패 후에도 연결은 유지되어야 함")
                .isTrue();
        assertThat(connector.isAuthenticated())
                .as("인증은 여전히 실패 상태")
                .isFalse();
    }
}
