package com.playhouse.connector;

/**
 * Connector 설정
 */
public final class ConnectorConfig {

    private final int sendBufferSize;
    private final int receiveBufferSize;
    private final int heartbeatIntervalMs;
    private final int requestTimeoutMs;
    private final boolean enableReconnect;
    private final int reconnectIntervalMs;

    private ConnectorConfig(Builder builder) {
        this.sendBufferSize = builder.sendBufferSize;
        this.receiveBufferSize = builder.receiveBufferSize;
        this.heartbeatIntervalMs = builder.heartbeatIntervalMs;
        this.requestTimeoutMs = builder.requestTimeoutMs;
        this.enableReconnect = builder.enableReconnect;
        this.reconnectIntervalMs = builder.reconnectIntervalMs;
    }

    /**
     * 빌더 생성
     *
     * @return ConnectorConfig Builder
     */
    public static Builder builder() {
        return new Builder();
    }

    /**
     * 기본 설정 생성
     *
     * @return 기본 설정
     */
    public static ConnectorConfig defaultConfig() {
        return builder().build();
    }

    // Getters
    public int getSendBufferSize() {
        return sendBufferSize;
    }

    public int getReceiveBufferSize() {
        return receiveBufferSize;
    }

    public int getHeartbeatIntervalMs() {
        return heartbeatIntervalMs;
    }

    public int getRequestTimeoutMs() {
        return requestTimeoutMs;
    }

    public boolean isEnableReconnect() {
        return enableReconnect;
    }

    public int getReconnectIntervalMs() {
        return reconnectIntervalMs;
    }

    @Override
    public String toString() {
        return String.format(
            "ConnectorConfig{sendBufferSize=%d, receiveBufferSize=%d, heartbeatIntervalMs=%d, " +
            "requestTimeoutMs=%d, enableReconnect=%s, reconnectIntervalMs=%d}",
            sendBufferSize, receiveBufferSize, heartbeatIntervalMs,
            requestTimeoutMs, enableReconnect, reconnectIntervalMs
        );
    }

    /**
     * ConnectorConfig Builder
     */
    public static final class Builder {
        private int sendBufferSize = 65536;         // 64KB
        private int receiveBufferSize = 262144;     // 256KB
        private int heartbeatIntervalMs = 10000;    // 10s
        private int requestTimeoutMs = 30000;       // 30s
        private boolean enableReconnect = false;
        private int reconnectIntervalMs = 5000;     // 5s

        private Builder() {
        }

        public Builder sendBufferSize(int sendBufferSize) {
            if (sendBufferSize <= 0) {
                throw new IllegalArgumentException("sendBufferSize must be positive");
            }
            this.sendBufferSize = sendBufferSize;
            return this;
        }

        public Builder receiveBufferSize(int receiveBufferSize) {
            if (receiveBufferSize <= 0) {
                throw new IllegalArgumentException("receiveBufferSize must be positive");
            }
            this.receiveBufferSize = receiveBufferSize;
            return this;
        }

        public Builder heartbeatIntervalMs(int heartbeatIntervalMs) {
            if (heartbeatIntervalMs < 0) {
                throw new IllegalArgumentException("heartbeatIntervalMs must be non-negative");
            }
            this.heartbeatIntervalMs = heartbeatIntervalMs;
            return this;
        }

        public Builder requestTimeoutMs(int requestTimeoutMs) {
            if (requestTimeoutMs <= 0) {
                throw new IllegalArgumentException("requestTimeoutMs must be positive");
            }
            this.requestTimeoutMs = requestTimeoutMs;
            return this;
        }

        public Builder enableReconnect(boolean enableReconnect) {
            this.enableReconnect = enableReconnect;
            return this;
        }

        public Builder reconnectIntervalMs(int reconnectIntervalMs) {
            if (reconnectIntervalMs <= 0) {
                throw new IllegalArgumentException("reconnectIntervalMs must be positive");
            }
            this.reconnectIntervalMs = reconnectIntervalMs;
            return this;
        }

        public ConnectorConfig build() {
            return new ConnectorConfig(this);
        }
    }
}
