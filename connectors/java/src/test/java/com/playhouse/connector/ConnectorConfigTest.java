package com.playhouse.connector;

import org.junit.jupiter.api.Test;

import static org.assertj.core.api.Assertions.*;

/**
 * ConnectorConfig 단위 테스트
 */
class ConnectorConfigTest {

    @Test
    void testDefaultConfig() {
        // When
        ConnectorConfig config = ConnectorConfig.defaultConfig();

        // Then
        assertThat(config.getSendBufferSize()).isEqualTo(65536);
        assertThat(config.getReceiveBufferSize()).isEqualTo(262144);
        assertThat(config.getHeartbeatIntervalMs()).isEqualTo(10000);
        assertThat(config.getRequestTimeoutMs()).isEqualTo(30000);
        assertThat(config.isEnableReconnect()).isFalse();
        assertThat(config.getReconnectIntervalMs()).isEqualTo(5000);
    }

    @Test
    void testBuilder() {
        // When
        ConnectorConfig config = ConnectorConfig.builder()
            .sendBufferSize(32768)
            .receiveBufferSize(131072)
            .heartbeatIntervalMs(5000)
            .requestTimeoutMs(10000)
            .enableReconnect(true)
            .reconnectIntervalMs(3000)
            .build();

        // Then
        assertThat(config.getSendBufferSize()).isEqualTo(32768);
        assertThat(config.getReceiveBufferSize()).isEqualTo(131072);
        assertThat(config.getHeartbeatIntervalMs()).isEqualTo(5000);
        assertThat(config.getRequestTimeoutMs()).isEqualTo(10000);
        assertThat(config.isEnableReconnect()).isTrue();
        assertThat(config.getReconnectIntervalMs()).isEqualTo(3000);
    }

    @Test
    void testBuilderWithDefaults() {
        // When
        ConnectorConfig config = ConnectorConfig.builder().build();

        // Then - 기본값 확인
        assertThat(config.getSendBufferSize()).isEqualTo(65536);
        assertThat(config.getReceiveBufferSize()).isEqualTo(262144);
    }

    @Test
    void testInvalidSendBufferSize() {
        // When/Then
        assertThatThrownBy(() ->
            ConnectorConfig.builder().sendBufferSize(0).build()
        )
            .isInstanceOf(IllegalArgumentException.class)
            .hasMessageContaining("sendBufferSize must be positive");
    }

    @Test
    void testInvalidReceiveBufferSize() {
        // When/Then
        assertThatThrownBy(() ->
            ConnectorConfig.builder().receiveBufferSize(-1).build()
        )
            .isInstanceOf(IllegalArgumentException.class)
            .hasMessageContaining("receiveBufferSize must be positive");
    }

    @Test
    void testInvalidHeartbeatInterval() {
        // When/Then
        assertThatThrownBy(() ->
            ConnectorConfig.builder().heartbeatIntervalMs(-1).build()
        )
            .isInstanceOf(IllegalArgumentException.class)
            .hasMessageContaining("heartbeatIntervalMs must be non-negative");
    }

    @Test
    void testZeroHeartbeatInterval() {
        // When - 0은 허용 (heartbeat 비활성화)
        ConnectorConfig config = ConnectorConfig.builder()
            .heartbeatIntervalMs(0)
            .build();

        // Then
        assertThat(config.getHeartbeatIntervalMs()).isEqualTo(0);
    }

    @Test
    void testInvalidRequestTimeout() {
        // When/Then
        assertThatThrownBy(() ->
            ConnectorConfig.builder().requestTimeoutMs(0).build()
        )
            .isInstanceOf(IllegalArgumentException.class)
            .hasMessageContaining("requestTimeoutMs must be positive");
    }

    @Test
    void testInvalidReconnectInterval() {
        // When/Then
        assertThatThrownBy(() ->
            ConnectorConfig.builder().reconnectIntervalMs(-1).build()
        )
            .isInstanceOf(IllegalArgumentException.class)
            .hasMessageContaining("reconnectIntervalMs must be positive");
    }

    @Test
    void testToString() {
        // Given
        ConnectorConfig config = ConnectorConfig.builder()
            .requestTimeoutMs(5000)
            .build();

        // When
        String str = config.toString();

        // Then
        assertThat(str).contains("ConnectorConfig");
        assertThat(str).contains("requestTimeoutMs=5000");
    }
}
