package com.playhouse.connector.internal;

import io.netty.bootstrap.Bootstrap;
import io.netty.buffer.ByteBuf;
import io.netty.buffer.Unpooled;
import io.netty.channel.*;
import io.netty.channel.nio.NioEventLoopGroup;
import io.netty.channel.socket.SocketChannel;
import io.netty.channel.socket.nio.NioSocketChannel;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.TimeUnit;
import java.util.function.Consumer;

/**
 * Netty 기반 TCP 연결
 * <p>
 * Netty의 NioEventLoopGroup과 Bootstrap을 사용한 비동기 I/O
 */
public final class TcpConnection {

    private static final Logger logger = LoggerFactory.getLogger(TcpConnection.class);

    private final int receiveBufferSize;
    private EventLoopGroup workerGroup;
    private Channel channel;
    private volatile boolean connected;
    private ByteBuffer receiveBuffer;

    /**
     * TcpConnection 생성자
     *
     * @param receiveBufferSize 수신 버퍼 크기
     */
    public TcpConnection(int receiveBufferSize) {
        this.receiveBufferSize = receiveBufferSize;
        this.receiveBuffer = ByteBuffer.allocate(receiveBufferSize).order(ByteOrder.LITTLE_ENDIAN);
        this.connected = false;
    }

    /**
     * 서버에 연결
     *
     * @param host         서버 호스트
     * @param port         서버 포트
     * @param timeoutMs    연결 타임아웃 (밀리초)
     * @return CompletableFuture<Boolean>
     */
    public CompletableFuture<Boolean> connectAsync(String host, int port, int timeoutMs) {
        CompletableFuture<Boolean> future = new CompletableFuture<>();

        // EventLoopGroup 생성 (이미 존재하지 않는 경우)
        if (workerGroup == null || workerGroup.isShutdown()) {
            workerGroup = new NioEventLoopGroup();
        }

        try {
            Bootstrap bootstrap = new Bootstrap();
            bootstrap.group(workerGroup)
                .channel(NioSocketChannel.class)
                .option(ChannelOption.SO_KEEPALIVE, true)
                .option(ChannelOption.TCP_NODELAY, true)
                .option(ChannelOption.CONNECT_TIMEOUT_MILLIS, timeoutMs)
                .handler(new ChannelInitializer<SocketChannel>() {
                    @Override
                    protected void initChannel(SocketChannel ch) {
                        // 핸들러는 startReceive에서 추가됨
                    }
                });

            logger.info("Connecting to {}:{}...", host, port);

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
    public boolean isConnected() {
        return connected && channel != null && channel.isActive();
    }

    /**
     * 데이터 전송
     *
     * @param data 전송할 데이터
     * @return CompletableFuture<Boolean>
     */
    public CompletableFuture<Boolean> sendAsync(ByteBuffer data) {
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
