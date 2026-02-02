package com.playhouse.connector.advanced;

import com.playhouse.connector.Connector;
import com.playhouse.connector.ConnectorConfig;
import com.playhouse.connector.Packet;
import com.playhouse.connector.support.BaseIntegrationTest;
import com.playhouse.connector.support.TestMessages.*;
import org.junit.jupiter.api.*;

import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.TimeUnit;

import static org.assertj.core.api.Assertions.*;

/**
 * A-02: 큰 페이로드 (LZ4 압축) 테스트
 * <p>
 * LargePayloadRequest를 통한 큰 데이터 송수신 및 압축 검증.
 * 서버는 1MB 페이로드를 반환하며, 전송 계층에서 LZ4 압축이 적용됨.
 * </p>
 */
@DisplayName("A-02: 큰 페이로드 테스트")
@Tag("Advanced")
@Tag("Compression")
class A02_LargePayloadTests extends BaseIntegrationTest {

    @Override
    @BeforeEach
    public void setUp() throws Exception {
        // 환경 변수에서 테스트 서버 설정 읽기
        host = System.getenv().getOrDefault("TEST_SERVER_HOST", "localhost");
        httpPort = Integer.parseInt(System.getenv().getOrDefault("TEST_SERVER_HTTP_PORT", "28080"));
        tcpPort = Integer.parseInt(System.getenv().getOrDefault("TEST_SERVER_TCP_PORT", "28001"));

        // 테스트 서버 클라이언트 초기화
        testServer = new com.playhouse.connector.support.TestServerClient(host, httpPort);

        // 큰 페이로드를 위해 타임아웃 증가
        connector = new Connector();
        connector.init(ConnectorConfig.builder()
                .requestTimeoutMs(30000)  // 30초
                .heartbeatIntervalMs(10000)
                .build());

        createStageAndConnect();
        authenticate("large-payload-user");
    }

    @Test
    @DisplayName("A-02-01: 1MB 페이로드를 수신할 수 있다")
    void largePayload1MBReceived() throws Exception {
        // Arrange
        LargePayloadRequest largePayloadRequest = new LargePayloadRequest(1048576); // 1MB

        // Act
        Packet requestPacket = Packet.builder("LargePayloadRequest")
                .payload(largePayloadRequest.toByteArray())
                .build();

        Packet responsePacket = connector.requestAsync(requestPacket)
                .get(30, TimeUnit.SECONDS);

        BenchmarkReply reply = BenchmarkReply.parseFrom(responsePacket.getPayload());

        // Assert
        assertThat(reply.payload).isNotNull();
        assertThat(reply.payload.length).isEqualTo(1048576);
    }

    @Test
    @DisplayName("A-02-02: 큰 페이로드의 데이터 무결성이 유지된다")
    void largePayloadDataIntegrity() throws Exception {
        // Arrange
        LargePayloadRequest largePayloadRequest = new LargePayloadRequest(1048576);

        // Act
        Packet requestPacket = Packet.builder("LargePayloadRequest")
                .payload(largePayloadRequest.toByteArray())
                .build();

        Packet responsePacket = connector.requestAsync(requestPacket)
                .get(30, TimeUnit.SECONDS);

        BenchmarkReply reply = BenchmarkReply.parseFrom(responsePacket.getPayload());

        // Assert - 서버가 순차적인 바이트 패턴으로 채움
        byte[] data = reply.payload;
        for (int i = 0; i < Math.min(1000, data.length); i++) {
            assertThat(data[i]).isEqualTo((byte) (i % 256));
        }
    }

    @Test
    @DisplayName("A-02-03: 연속된 큰 페이로드 요청을 처리할 수 있다")
    void largePayloadSequentialRequests() throws Exception {
        // Arrange & Act
        List<Integer> results = new ArrayList<>();

        for (int i = 0; i < 3; i++) {
            LargePayloadRequest request = new LargePayloadRequest(1048576);
            Packet requestPacket = Packet.builder("LargePayloadRequest")
                    .payload(request.toByteArray())
                    .build();

            Packet responsePacket = connector.requestAsync(requestPacket)
                    .get(30, TimeUnit.SECONDS);

            BenchmarkReply reply = BenchmarkReply.parseFrom(responsePacket.getPayload());
            results.add(reply.payload.length);
        }

        // Assert
        assertThat(results).hasSize(3);
        assertThat(results).allMatch(size -> size == 1048576);
    }

    @Test
    @DisplayName("A-02-04: 큰 요청 페이로드를 전송할 수 있다")
    void largePayloadSendLargeRequest() throws Exception {
        // Arrange - 큰 데이터를 담은 Echo 요청
        String largeContent = "A".repeat(100000); // 100KB 문자열
        EchoRequest echoRequest = new EchoRequest(largeContent, 1);

        // Act
        Packet requestPacket = Packet.builder("EchoRequest")
                .payload(echoRequest.toByteArray())
                .build();

        Packet responsePacket = connector.requestAsync(requestPacket)
                .get(30, TimeUnit.SECONDS);

        EchoReply echoReply = EchoReply.parseFrom(responsePacket.getPayload());

        // Assert
        assertThat(echoReply.content).isEqualTo(largeContent);
    }

    @Test
    @DisplayName("A-02-05: 병렬 큰 페이로드 요청을 처리할 수 있다")
    void largePayloadParallelRequests() throws Exception {
        // Arrange & Act
        List<CompletableFuture<Packet>> tasks = new ArrayList<>();

        for (int i = 0; i < 3; i++) {
            LargePayloadRequest request = new LargePayloadRequest(524288); // 512KB each
            Packet packet = Packet.builder("LargePayloadRequest")
                    .payload(request.toByteArray())
                    .build();
            tasks.add(connector.requestAsync(packet));
        }

        CompletableFuture<Void> allTasks = CompletableFuture.allOf(
                tasks.toArray(new CompletableFuture[0])
        );
        allTasks.get(30, TimeUnit.SECONDS);

        // Assert
        assertThat(tasks).hasSize(3);
        for (CompletableFuture<Packet> task : tasks) {
            Packet response = task.get();
            BenchmarkReply reply = BenchmarkReply.parseFrom(response.getPayload());
            // 서버가 항상 1MB를 반환하므로 1MB 확인
            assertThat(reply.payload.length).isEqualTo(1048576);
        }
    }
}
