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
    private final boolean useWebsocket;
    private final boolean useSsl;
    private final String webSocketPath;
    private final boolean skipServerCertificateValidation;
    private final int heartbeatTimeoutMs;
    private final int connectionIdleTimeoutMs;

    private ConnectorConfig(Builder builder) {
        this.sendBufferSize = builder.sendBufferSize;
        this.receiveBufferSize = builder.receiveBufferSize;
        this.heartbeatIntervalMs = builder.heartbeatIntervalMs;
        this.requestTimeoutMs = builder.requestTimeoutMs;
        this.enableReconnect = builder.enableReconnect;
        this.reconnectIntervalMs = builder.reconnectIntervalMs;
        this.useWebsocket = builder.useWebsocket;
        this.useSsl = builder.useSsl;
        this.webSocketPath = builder.webSocketPath;
        this.skipServerCertificateValidation = builder.skipServerCertificateValidation;
        this.heartbeatTimeoutMs = builder.heartbeatTimeoutMs;
        this.connectionIdleTimeoutMs = builder.connectionIdleTimeoutMs;
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

    public boolean isUseWebsocket() {
        return useWebsocket;
    }

    public boolean isUseSsl() {
        return useSsl;
    }

    public String getWebSocketPath() {
        return webSocketPath;
    }

    public boolean isSkipServerCertificateValidation() {
        return skipServerCertificateValidation;
    }

    public int getHeartbeatTimeoutMs() {
        return heartbeatTimeoutMs;
    }

    public int getConnectionIdleTimeoutMs() {
        return connectionIdleTimeoutMs;
    }

    @Override
    public String toString() {
        return String.format(
            "ConnectorConfig{sendBufferSize=%d, receiveBufferSize=%d, heartbeatIntervalMs=%d, " +
            "requestTimeoutMs=%d, enableReconnect=%s, reconnectIntervalMs=%d, useWebsocket=%s, " +
            "useSsl=%s, webSocketPath='%s', skipServerCertificateValidation=%s, " +
            "heartbeatTimeoutMs=%d, connectionIdleTimeoutMs=%d}",
            sendBufferSize, receiveBufferSize, heartbeatIntervalMs,
            requestTimeoutMs, enableReconnect, reconnectIntervalMs, useWebsocket,
            useSsl, webSocketPath, skipServerCertificateValidation,
            heartbeatTimeoutMs, connectionIdleTimeoutMs
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
        private boolean useWebsocket = false;
        private boolean useSsl = false;
        private String webSocketPath = "/ws";
        private boolean skipServerCertificateValidation = false;
        private int heartbeatTimeoutMs = 30000;     // 30s
        private int connectionIdleTimeoutMs = 30000; // 30s

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

        public Builder useWebsocket(boolean useWebsocket) {
            this.useWebsocket = useWebsocket;
            return this;
        }

        public Builder useSsl(boolean useSsl) {
            this.useSsl = useSsl;
            return this;
        }

        public Builder webSocketPath(String webSocketPath) {
            if (webSocketPath == null || webSocketPath.isEmpty()) {
                throw new IllegalArgumentException("webSocketPath cannot be null or empty");
            }
            this.webSocketPath = webSocketPath;
            return this;
        }

        public Builder skipServerCertificateValidation(boolean skipServerCertificateValidation) {
            this.skipServerCertificateValidation = skipServerCertificateValidation;
            return this;
        }

        public Builder heartbeatTimeoutMs(int heartbeatTimeoutMs) {
            if (heartbeatTimeoutMs <= 0) {
                throw new IllegalArgumentException("heartbeatTimeoutMs must be positive");
            }
            this.heartbeatTimeoutMs = heartbeatTimeoutMs;
            return this;
        }

        public Builder connectionIdleTimeoutMs(int connectionIdleTimeoutMs) {
            if (connectionIdleTimeoutMs <= 0) {
                throw new IllegalArgumentException("connectionIdleTimeoutMs must be positive");
            }
            this.connectionIdleTimeoutMs = connectionIdleTimeoutMs;
            return this;
        }

        public ConnectorConfig build() {
            return new ConnectorConfig(this);
        }
    }
}
