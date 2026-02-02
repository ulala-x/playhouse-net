package com.playhouse.connector.internal;

import com.playhouse.connector.ConnectorConfig;
import io.netty.bootstrap.Bootstrap;
import io.netty.buffer.ByteBuf;
import io.netty.buffer.Unpooled;
import io.netty.channel.*;
import io.netty.channel.nio.NioEventLoopGroup;
import io.netty.channel.socket.SocketChannel;
import io.netty.channel.socket.nio.NioSocketChannel;
import io.netty.handler.ssl.SslContext;
import io.netty.handler.ssl.SslContextBuilder;
import io.netty.handler.ssl.util.InsecureTrustManagerFactory;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import javax.net.ssl.SSLException;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.concurrent.CompletableFuture;
import java.util.function.Consumer;

/**
 * Netty 기반 TCP 연결
 * <p>
 * Netty의 NioEventLoopGroup과 Bootstrap을 사용한 비동기 I/O
 * SSL/TLS 연결 지원
 */
public final class TcpConnection implements IConnection {

    private static final Logger logger = LoggerFactory.getLogger(TcpConnection.class);

    private final ConnectorConfig config;
    private EventLoopGroup workerGroup;
    private Channel channel;
    private volatile boolean connected;
    private ByteBuffer receiveBuffer;

    /**
     * TcpConnection 생성자
     *
     * @param config Connector 설정
     */
    public TcpConnection(ConnectorConfig config) {
        this.config = config;
        this.receiveBuffer = ByteBuffer.allocate(config.getReceiveBufferSize()).order(ByteOrder.LITTLE_ENDIAN);
        this.connected = false;
    }

    /**
     * 서버에 연결
     *
     * @param host      서버 호스트
     * @param port      서버 포트
     * @param useSsl    SSL/TLS 사용 여부
     * @param timeoutMs 연결 타임아웃 (밀리초)
     * @return CompletableFuture<Boolean>
     */
    @Override
    public CompletableFuture<Boolean> connectAsync(String host, int port, boolean useSsl, int timeoutMs) {
        CompletableFuture<Boolean> future = new CompletableFuture<>();

        // EventLoopGroup 생성 (이미 존재하지 않는 경우)
        if (workerGroup == null || workerGroup.isShutdown()) {
            workerGroup = new NioEventLoopGroup();
        }

        try {
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

                        // 핸들러는 startReceive에서 추가됨
                    }
                });

            logger.info("Connecting to {}:{} (SSL: {})...", host, port, useSsl);

            // 비동기 연결
            ChannelFuture connectFuture = bootstrap.connect(host, port);
            connectFuture.addListener((ChannelFutureListener) channelFuture -> {
                if (channelFuture.isSuccess()) {
                    channel = channelFuture.channel();
                    connected = true;
                    logger.info("Connected to {}:{}", host, port);
                    future.complete(true);
                } else {
                    connected = false;
                    logger.error("Connection failed to {}:{}", host, port, channelFuture.cause());
                    future.complete(false);
                }
            });

        } catch (Exception e) {
            logger.error("Failed to create bootstrap", e);
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
        if (data == null || !data.hasRemaining()) {
            logger.warn("Attempted to send null or empty data");
            return CompletableFuture.completedFuture(false);
        }

        if (!isConnected()) {
            return CompletableFuture.completedFuture(false);
        }

        CompletableFuture<Boolean> future = new CompletableFuture<>();

        try {
            // ByteBuffer를 Netty ByteBuf로 변환
            ByteBuf byteBuf = Unpooled.wrappedBuffer(data);

            // 비동기 전송
            ChannelFuture writeFuture = channel.writeAndFlush(byteBuf);
            writeFuture.addListener((ChannelFutureListener) channelFuture -> {
                if (channelFuture.isSuccess()) {
                    if (logger.isDebugEnabled()) {
                        logger.debug("Sent {} bytes", data.remaining());
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

        // 기존 핸들러 제거 후 새 핸들러 추가
        ChannelPipeline pipeline = channel.pipeline();
        pipeline.addLast(new NettyReceiveHandler(onReceive, onClosed));
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
                logger.info("Disconnecting...");
                ChannelFuture closeFuture = channel.close();
                closeFuture.addListener((ChannelFutureListener) channelFuture -> {
                    connected = false;
                    logger.info("Disconnected");
                    future.complete(null);
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
     * Netty 수신 핸들러
     */
    private class NettyReceiveHandler extends ChannelInboundHandlerAdapter {

        private final Consumer<ByteBuffer> onReceive;
        private final Runnable onClosed;

        NettyReceiveHandler(Consumer<ByteBuffer> onReceive, Runnable onClosed) {
            this.onReceive = onReceive;
            this.onClosed = onClosed;
        }

        @Override
        public void channelRead(ChannelHandlerContext ctx, Object msg) {
            if (msg instanceof ByteBuf) {
                ByteBuf byteBuf = (ByteBuf) msg;
                try {
                    int readableBytes = byteBuf.readableBytes();
                    if (readableBytes > 0) {
                        if (logger.isDebugEnabled()) {
                            logger.debug("Received {} bytes", readableBytes);
                        }

                        // ByteBuf를 ByteBuffer로 변환하여 기존 버퍼에 추가
                        ensureBufferCapacity(receiveBuffer.position() + readableBytes);
                        byteBuf.readBytes(receiveBuffer.array(), receiveBuffer.position(), readableBytes);
                        receiveBuffer.position(receiveBuffer.position() + readableBytes);

                        // 버퍼를 읽기 모드로 전환
                        receiveBuffer.flip();

                        // 패킷 처리
                        try {
                            onReceive.accept(receiveBuffer);
                        } catch (Exception e) {
                            logger.error("Error processing received data", e);
                        }

                        // 버퍼 정리
                        receiveBuffer.compact();
                    }
                } finally {
                    byteBuf.release();
                }
            }
        }

        @Override
        public void channelInactive(ChannelHandlerContext ctx) {
            logger.info("Connection closed by server");
            connected = false;
            onClosed.run();
        }

        @Override
        public void exceptionCaught(ChannelHandlerContext ctx, Throwable cause) {
            logger.error("Exception caught in channel", cause);
            connected = false;
            onClosed.run();
            ctx.close();
        }
    }
}
