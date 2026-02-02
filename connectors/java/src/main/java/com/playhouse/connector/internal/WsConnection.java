package com.playhouse.connector.internal;

import com.playhouse.connector.ConnectorConfig;
import io.netty.bootstrap.Bootstrap;
import io.netty.buffer.ByteBuf;
import io.netty.buffer.Unpooled;
import io.netty.channel.*;
import io.netty.channel.nio.NioEventLoopGroup;
import io.netty.channel.socket.SocketChannel;
import io.netty.channel.socket.nio.NioSocketChannel;
import io.netty.handler.codec.http.DefaultHttpHeaders;
import io.netty.handler.codec.http.HttpClientCodec;
import io.netty.handler.codec.http.HttpObjectAggregator;
import io.netty.handler.codec.http.websocketx.*;
import io.netty.handler.ssl.SslContext;
import io.netty.handler.ssl.SslContextBuilder;
import io.netty.handler.ssl.util.InsecureTrustManagerFactory;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import javax.net.ssl.SSLException;
import java.net.URI;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.concurrent.CompletableFuture;
import java.util.function.Consumer;

/**
 * Netty 기반 WebSocket 연결
 * <p>
 * Netty WebSocket 클라이언트를 사용한 비동기 I/O
 * Binary WebSocket 프레임으로 동일한 패킷 프로토콜 사용
 */
public final class WsConnection implements IConnection {

    private static final Logger logger = LoggerFactory.getLogger(WsConnection.class);

    private final ConnectorConfig config;
    private EventLoopGroup workerGroup;
    private Channel channel;
    private WebSocketClientHandshaker handshaker;
    private volatile boolean connected;
    private ByteBuffer receiveBuffer;
    private CompletableFuture<Boolean> handshakeFuture;

    /**
     * WsConnection 생성자
     *
     * @param config Connector 설정
     */
    public WsConnection(ConnectorConfig config) {
        this.config = config;
        this.receiveBuffer = ByteBuffer.allocate(config.getReceiveBufferSize()).order(ByteOrder.LITTLE_ENDIAN);
        this.connected = false;
    }

    /**
     * 서버에 연결
     *
     * @param host      서버 호스트
     * @param port      서버 포트
     * @param useSsl    SSL/TLS 사용 여부 (wss://)
     * @param timeoutMs 연결 타임아웃 (밀리초)
     * @return CompletableFuture<Boolean>
     */
    @Override
    public CompletableFuture<Boolean> connectAsync(String host, int port, boolean useSsl, int timeoutMs) {
        CompletableFuture<Boolean> future = new CompletableFuture<>();
        handshakeFuture = new CompletableFuture<>();

        // EventLoopGroup 생성 (이미 존재하지 않는 경우)
        if (workerGroup == null || workerGroup.isShutdown()) {
            workerGroup = new NioEventLoopGroup();
        }

        try {
            // WebSocket URI 생성
            String scheme = useSsl ? "wss" : "ws";
            String path = config.getWebSocketPath();
            if (!path.startsWith("/")) {
                path = "/" + path;
            }
            URI uri = new URI(scheme + "://" + host + ":" + port + path);

            // SSL 컨텍스트 생성
            final SslContext sslContext;
            if (useSsl) {
                try {
                    SslContextBuilder sslBuilder = SslContextBuilder.forClient();
                    if (config.isSkipServerCertificateValidation()) {
                        // 테스트용: 인증서 검증 스킵
                        sslBuilder.trustManager(InsecureTrustManagerFactory.INSTANCE);
                        logger.warn("SSL certificate validation is disabled. This should only be used for testing!");
                    }
                    sslContext = sslBuilder.build();
                } catch (SSLException e) {
                    logger.error("Failed to create SSL context", e);
                    future.complete(false);
                    return future;
                }
            } else {
                sslContext = null;
            }

            // WebSocket handshaker 생성
            handshaker = WebSocketClientHandshakerFactory.newHandshaker(
                uri,
                WebSocketVersion.V13,
                null,
                true,
                new DefaultHttpHeaders(),
                65536 // Max frame payload length
            );

            Bootstrap bootstrap = new Bootstrap();
            bootstrap.group(workerGroup)
                .channel(NioSocketChannel.class)
                .option(ChannelOption.SO_KEEPALIVE, true)
                .option(ChannelOption.TCP_NODELAY, true)
                .option(ChannelOption.CONNECT_TIMEOUT_MILLIS, timeoutMs)
                .handler(new ChannelInitializer<SocketChannel>() {
                    @Override
                    protected void initChannel(SocketChannel ch) {
                        ChannelPipeline pipeline = ch.pipeline();

                        // SSL 핸들러 추가 (필요시)
                        if (sslContext != null) {
                            pipeline.addLast(sslContext.newHandler(ch.alloc(), host, port));
                        }

                        // HTTP 핸들러
                        pipeline.addLast(new HttpClientCodec());
                        pipeline.addLast(new HttpObjectAggregator(8192));

                        // WebSocket 핸들러 (handshake만 담당)
                        pipeline.addLast(new WebSocketClientProtocolHandler(handshaker));

                        // 핸드셰이크 완료 이벤트 핸들러 (연결 시점에 추가)
                        pipeline.addLast(new HandshakeEventHandler());
                    }
                });

            logger.info("Connecting to {} (SSL: {})...", uri, useSsl);

            // 비동기 연결
            ChannelFuture connectFuture = bootstrap.connect(host, port);
            connectFuture.addListener((ChannelFutureListener) channelFuture -> {
                if (channelFuture.isSuccess()) {
                    channel = channelFuture.channel();

                    // WebSocket handshake 완료 대기
                    handshakeFuture.thenAccept(success -> {
                        if (success) {
                            connected = true;
                            logger.info("WebSocket connected to {}", uri);
                            future.complete(true);
                        } else {
                            connected = false;
                            logger.error("WebSocket handshake failed");
                            future.complete(false);
                        }
                    });
                } else {
                    connected = false;
                    logger.error("Connection failed to {}", uri, channelFuture.cause());
                    future.complete(false);
                }
            });

        } catch (Exception e) {
            logger.error("Failed to create WebSocket connection", e);
            future.complete(false);
        }

        return future;
    }

    /**
     * 연결 상태 확인
     *
     * @return 연결되어 있으면 true
     */
    @Override
    public boolean isConnected() {
        return connected && channel != null && channel.isActive();
    }

    /**
     * 데이터 전송
     *
     * @param data 전송할 데이터
     * @return CompletableFuture<Boolean>
     */
    @Override
    public CompletableFuture<Boolean> sendAsync(ByteBuffer data) {
        if (!isConnected()) {
            return CompletableFuture.completedFuture(false);
        }

        if (data == null || !data.hasRemaining()) {
            logger.warn("Attempted to send null or empty data");
            return CompletableFuture.completedFuture(false);
        }

        CompletableFuture<Boolean> future = new CompletableFuture<>();

        try {
            // ByteBuffer를 복사하여 Netty ByteBuf로 변환 (원본 ByteBuffer 보호)
            int dataSize = data.remaining();
            byte[] dataBytes = new byte[dataSize];
            data.get(dataBytes);
            ByteBuf byteBuf = Unpooled.wrappedBuffer(dataBytes);

            // Binary WebSocket Frame으로 전송
            BinaryWebSocketFrame frame = new BinaryWebSocketFrame(byteBuf);

            // 비동기 전송
            ChannelFuture writeFuture = channel.writeAndFlush(frame);
            writeFuture.addListener((ChannelFutureListener) channelFuture -> {
                if (channelFuture.isSuccess()) {
                    if (logger.isDebugEnabled()) {
                        logger.debug("Sent {} bytes via WebSocket", dataSize);
                    }
                    future.complete(true);
                } else {
                    logger.error("Send failed", channelFuture.cause());
                    future.complete(false);
                }
            });
        } catch (Exception e) {
            logger.error("Failed to send data", e);
            future.complete(false);
        }

        return future;
    }

    /**
     * 데이터 수신 시작 (비동기 루프)
     *
     * @param onReceive 수신 콜백
     * @param onClosed  연결 종료 콜백
     */
    @Override
    public void startReceive(Consumer<ByteBuffer> onReceive, Runnable onClosed) {
        if (!isConnected()) {
            logger.warn("Cannot start receive: not connected");
            return;
        }

        // WebSocket 수신 핸들러 추가
        ChannelPipeline pipeline = channel.pipeline();
        pipeline.addLast(new WebSocketReceiveHandler(onReceive, onClosed));
    }

    /**
     * 연결 종료
     *
     * @return CompletableFuture<Void>
     */
    @Override
    public CompletableFuture<Void> disconnectAsync() {
        CompletableFuture<Void> future = new CompletableFuture<>();

        try {
            if (channel != null && channel.isActive()) {
                logger.info("Disconnecting WebSocket...");

                // WebSocket Close Frame 전송
                CloseWebSocketFrame closeFrame = new CloseWebSocketFrame(
                    WebSocketCloseStatus.NORMAL_CLOSURE,
                    "Client disconnect"
                );

                ChannelFuture closeFuture = channel.writeAndFlush(closeFrame);
                closeFuture.addListener((ChannelFutureListener) channelFuture -> {
                    // 채널 닫기
                    channel.close().addListener(cf -> {
                        connected = false;
                        logger.info("WebSocket disconnected");
                        future.complete(null);
                    });
                });
            } else {
                connected = false;
                future.complete(null);
            }
        } catch (Exception e) {
            logger.error("Error during disconnect", e);
            connected = false;
            future.complete(null);
        }

        return future;
    }

    /**
     * 수신 버퍼 크기 조정 (필요시)
     */
    public void ensureBufferCapacity(int requiredSize) {
        if (receiveBuffer.capacity() < requiredSize) {
            logger.debug("Expanding receive buffer from {} to {}", receiveBuffer.capacity(), requiredSize);
            ByteBuffer newBuffer = ByteBuffer.allocate(requiredSize).order(ByteOrder.LITTLE_ENDIAN);
            receiveBuffer.flip();
            newBuffer.put(receiveBuffer);
            receiveBuffer = newBuffer;
        }
    }

    /**
     * EventLoopGroup 종료
     */
    @Override
    public void shutdown() {
        if (workerGroup != null && !workerGroup.isShutdown()) {
            logger.info("Shutting down EventLoopGroup");
            workerGroup.shutdownGracefully();
        }
    }

    /**
     * WebSocket 수신 핸들러
     */
    private class WebSocketReceiveHandler extends SimpleChannelInboundHandler<WebSocketFrame> {

        private final Consumer<ByteBuffer> onReceive;
        private final Runnable onClosed;

        WebSocketReceiveHandler(Consumer<ByteBuffer> onReceive, Runnable onClosed) {
            this.onReceive = onReceive;
            this.onClosed = onClosed;
        }

        @Override
        public void userEventTriggered(ChannelHandlerContext ctx, Object evt) throws Exception {
            // 핸드셰이크 이벤트는 HandshakeEventHandler에서 처리
            super.userEventTriggered(ctx, evt);
        }

        @Override
        protected void channelRead0(ChannelHandlerContext ctx, WebSocketFrame frame) {
            if (frame instanceof BinaryWebSocketFrame) {
                ByteBuf byteBuf = frame.content();
                int readableBytes = byteBuf.readableBytes();

                if (readableBytes <= 0) {
                    return;
                }

                if (logger.isDebugEnabled()) {
                    logger.debug("Received {} bytes via WebSocket", readableBytes);
                }

                synchronized (receiveBuffer) {
                    boolean bufferFlipped = false;
                    try {
                        // ByteBuf를 ByteBuffer로 변환하여 기존 버퍼에 추가
                        ensureBufferCapacity(receiveBuffer.position() + readableBytes);

                        // ByteBuf의 데이터를 안전하게 복사
                        byte[] tempBuffer = new byte[readableBytes];
                        byteBuf.readBytes(tempBuffer);
                        receiveBuffer.put(tempBuffer);

                        // 버퍼를 읽기 모드로 전환
                        receiveBuffer.flip();
                        bufferFlipped = true;

                        // 패킷 처리
                        onReceive.accept(receiveBuffer);
                    } catch (Exception e) {
                        logger.error("Error processing WebSocket frame", e);
                    } finally {
                        // 버퍼 정리 (예외 발생 여부와 관계없이 항상 실행)
                        if (bufferFlipped) {
                            receiveBuffer.compact();
                        } else {
                            // flip 전에 예외가 발생한 경우 버퍼 상태 복구
                            receiveBuffer.clear();
                        }
                    }
                }
            } else if (frame instanceof TextWebSocketFrame) {
                logger.warn("Received text frame, but expected binary frame");
            } else if (frame instanceof PongWebSocketFrame) {
                if (logger.isDebugEnabled()) {
                    logger.debug("Received pong frame");
                }
            } else if (frame instanceof CloseWebSocketFrame) {
                logger.info("Received close frame from server");
                ctx.close();
            }
        }

        @Override
        public void channelInactive(ChannelHandlerContext ctx) {
            logger.info("WebSocket connection closed by server");
            connected = false;
            // Complete handshakeFuture if it's still pending (prevents hang on connection failure before handshake)
            if (handshakeFuture != null && !handshakeFuture.isDone()) {
                handshakeFuture.complete(false);
            }
            onClosed.run();
        }

        @Override
        public void exceptionCaught(ChannelHandlerContext ctx, Throwable cause) {
            logger.error("Exception caught in WebSocket channel", cause);
            connected = false;
            // Complete handshakeFuture if it's still pending (prevents hang on connection failure before handshake)
            if (handshakeFuture != null && !handshakeFuture.isDone()) {
                handshakeFuture.complete(false);
            }
            onClosed.run();
            ctx.close();
        }
    }

    /**
     * 핸드셰이크 이벤트만 처리하는 핸들러
     * 연결 시점에 파이프라인에 추가되어 핸드셰이크 완료를 감지
     */
    private class HandshakeEventHandler extends ChannelInboundHandlerAdapter {

        @Override
        public void userEventTriggered(ChannelHandlerContext ctx, Object evt) throws Exception {
            if (evt == WebSocketClientProtocolHandler.ClientHandshakeStateEvent.HANDSHAKE_COMPLETE) {
                // Handshake 완료
                logger.info("WebSocket handshake completed");
                handshakeFuture.complete(true);
            } else if (evt == WebSocketClientProtocolHandler.ClientHandshakeStateEvent.HANDSHAKE_TIMEOUT) {
                // Handshake 타임아웃
                logger.error("WebSocket handshake timeout");
                handshakeFuture.complete(false);
            }
            super.userEventTriggered(ctx, evt);
        }

        @Override
        public void exceptionCaught(ChannelHandlerContext ctx, Throwable cause) throws Exception {
            logger.error("Exception during WebSocket handshake", cause);
            if (handshakeFuture != null && !handshakeFuture.isDone()) {
                handshakeFuture.complete(false);
            }
            super.exceptionCaught(ctx, cause);
        }

        @Override
        public void channelInactive(ChannelHandlerContext ctx) throws Exception {
            // 핸드셰이크 완료 전에 연결이 끊긴 경우
            if (handshakeFuture != null && !handshakeFuture.isDone()) {
                logger.error("Connection closed before handshake completed");
                handshakeFuture.complete(false);
            }
            super.channelInactive(ctx);
        }
    }
}
