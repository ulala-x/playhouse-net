package com.playhouse.connector.core;

import com.playhouse.connector.Packet;
import com.playhouse.connector.support.BaseIntegrationTest;
import com.playhouse.connector.support.TestMessages;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;

import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.TimeUnit;
import java.util.stream.Collectors;
import java.util.stream.IntStream;

import static org.assertj.core.api.Assertions.assertThat;

/**
 * C-05: Echo Request-Response 테스트
 * <p>
 * 기본적인 Request-Response 패턴이 정상 동작하는지 검증합니다.
 * EchoRequest를 보내고 EchoReply를 받아 내용이 일치하는지 확인합니다.
 * </p>
 */
@DisplayName("C-05: Echo Request-Response Tests")
public class C05_EchoRequestResponseTests extends BaseIntegrationTest {

    @Test
    @DisplayName("C-05-01: Echo Request-Response가 정상 동작한다")
    public void echoRequest_sendAndReceive_returnsMatchingReply() throws Exception {
        // Given: 연결 및 인증 완료
        createStageAndConnect();
        authenticate("echoUser");

        String testContent = "Hello PlayHouse!";
        int testSequence = 42;

        // When: Echo 요청
        TestMessages.EchoReply echoReply = echo(testContent, testSequence);

        // Then: 응답이 요청과 일치해야 함
        assertThat(echoReply).isNotNull();
        assertThat(echoReply.content).isEqualTo(testContent);
        assertThat(echoReply.sequence).isEqualTo(testSequence);
        assertThat(echoReply.processedAt).isGreaterThan(0);
    }

    @Test
    @DisplayName("C-05-02: RequestAsync로 Echo 요청할 수 있다")
    public void requestAsync_withEchoRequest_returnsEchoReply() throws Exception {
        // Given: 연결 및 인증 완료
        createStageAndConnect();
        authenticate("asyncUser");

        TestMessages.EchoRequest echoRequest = new TestMessages.EchoRequest("Async Echo Test", 1);

        // When: RequestAsync 호출
        Packet requestPacket = Packet.builder("EchoRequest")
                .payload(echoRequest.toByteArray())
                .build();

        Packet responsePacket = connector.requestAsync(requestPacket)
                .get(5, TimeUnit.SECONDS);

        // Then: 올바른 응답을 받아야 함
        assertThat(responsePacket).isNotNull();
        assertThat(responsePacket.getMsgId()).isEqualTo("EchoReply");

        TestMessages.EchoReply echoReply = TestMessages.EchoReply.parseFrom(responsePacket.getPayload());
        assertThat(echoReply.content).isEqualTo("Async Echo Test");
        assertThat(echoReply.sequence).isEqualTo(1);
    }

    @Test
    @DisplayName("C-05-03: Request 콜백 방식으로 Echo 요청할 수 있다")
    public void request_withCallback_receivesEchoReply() throws Exception {
        // Given: 연결 및 인증 완료
        createStageAndConnect();
        authenticate("callbackUser");

        TestMessages.EchoRequest echoRequest = new TestMessages.EchoRequest("Callback Echo Test", 100);

        CompletableFuture<TestMessages.EchoReply> future = new CompletableFuture<>();

        // When: Request 콜백 방식 호출
        Packet requestPacket = Packet.builder("EchoRequest")
                .payload(echoRequest.toByteArray())
                .build();

        connector.request(requestPacket, responsePacket -> {
            try {
                TestMessages.EchoReply echoReply = TestMessages.EchoReply.parseFrom(responsePacket.getPayload());
                future.complete(echoReply);
            } catch (Exception e) {
                future.completeExceptionally(e);
            }
        });

        // 콜백 대기 (MainThreadAction 호출하면서 최대 5초)
        TestMessages.EchoReply echoReply = waitWithMainThreadAction(future, 5000);

        // Then: 콜백이 호출되고 올바른 응답을 받아야 함
        assertThat(echoReply.content).isEqualTo("Callback Echo Test");
        assertThat(echoReply.sequence).isEqualTo(100);
    }

    @Test
    @DisplayName("C-05-04: 연속된 Echo 요청을 처리할 수 있다")
    public void echoRequest_sequential_allSucceed() throws Exception {
        // Given: 연결 및 인증 완료
        createStageAndConnect();
        authenticate("sequentialUser");

        // When: 5개의 연속된 Echo 요청
        List<TestMessages.EchoReply> replies = new ArrayList<>();
        for (int i = 1; i <= 5; i++) {
            TestMessages.EchoReply reply = echo("Message " + i, i);
            replies.add(reply);
        }

        // Then: 모든 응답이 순서대로 올바르게 반환되어야 함
        assertThat(replies).hasSize(5);
        for (int i = 0; i < 5; i++) {
            assertThat(replies.get(i).content).isEqualTo("Message " + (i + 1));
            assertThat(replies.get(i).sequence).isEqualTo(i + 1);
        }
    }

    @Test
    @DisplayName("C-05-05: 병렬 Echo 요청을 처리할 수 있다")
    public void echoRequest_parallel_allSucceed() throws Exception {
        // Given: 연결 및 인증 완료
        createStageAndConnect();
        authenticate("parallelUser");

        // When: 10개의 병렬 Echo 요청
        List<CompletableFuture<TestMessages.EchoReply>> tasks = IntStream.range(1, 11)
                .mapToObj(i -> {
                    TestMessages.EchoRequest echoRequest = new TestMessages.EchoRequest("Parallel " + i, i);
                    Packet requestPacket = Packet.builder("EchoRequest")
                            .payload(echoRequest.toByteArray())
                            .build();

                    return connector.requestAsync(requestPacket)
                            .thenApply(responsePacket -> {
                                try {
                                    return TestMessages.EchoReply.parseFrom(responsePacket.getPayload());
                                } catch (Exception e) {
                                    throw new RuntimeException(e);
                                }
                            });
                })
                .collect(Collectors.toList());

        List<TestMessages.EchoReply> replies = CompletableFuture.allOf(tasks.toArray(new CompletableFuture[0]))
                .thenApply(v -> tasks.stream()
                        .map(CompletableFuture::join)
                        .collect(Collectors.toList()))
                .get(10, TimeUnit.SECONDS);

        // Then: 모든 응답이 올바르게 반환되어야 함
        assertThat(replies).hasSize(10);
        for (int i = 1; i <= 10; i++) {
            int sequence = i;
            TestMessages.EchoReply reply = replies.stream()
                    .filter(r -> r.sequence == sequence)
                    .findFirst()
                    .orElse(null);

            assertThat(reply).isNotNull();
            assertThat(reply.content).isEqualTo("Parallel " + sequence);
        }
    }

    @Test
    @DisplayName("C-05-06: 빈 문자열도 에코할 수 있다")
    public void echoRequest_withEmptyString_returnsEmptyString() throws Exception {
        // Given: 연결 및 인증 완료
        createStageAndConnect();
        authenticate("emptyUser");

        // When: 빈 문자열로 Echo 요청
        TestMessages.EchoReply echoReply = echo("", 999);

        // Then: 빈 문자열이 에코되어야 함
        assertThat(echoReply.content).isEmpty();
        assertThat(echoReply.sequence).isEqualTo(999);
    }

    @Test
    @DisplayName("C-05-07: 긴 문자열도 에코할 수 있다")
    public void echoRequest_withLongString_returnsFullString() throws Exception {
        // Given: 연결 및 인증 완료
        createStageAndConnect();
        authenticate("longUser");

        // 1KB 문자열 생성
        String longContent = "A".repeat(1024);

        // When: 긴 문자열로 Echo 요청
        TestMessages.EchoReply echoReply = echo(longContent, 1);

        // Then: 전체 문자열이 에코되어야 함
        assertThat(echoReply.content).isEqualTo(longContent);
        assertThat(echoReply.content.length()).isEqualTo(1024);
    }

    @Test
    @DisplayName("C-05-08: 유니코드 문자열도 에코할 수 있다")
    public void echoRequest_withUnicodeString_returnsCorrectString() throws Exception {
        // Given: 연결 및 인증 완료
        createStageAndConnect();
        authenticate("unicodeUser");

        String unicodeContent = "안녕하세요 🎮 こんにちは 你好";

        // When: 유니코드 문자열로 Echo 요청
        TestMessages.EchoReply echoReply = echo(unicodeContent, 1);

        // Then: 유니코드 문자열이 올바르게 에코되어야 함
        assertThat(echoReply.content).isEqualTo(unicodeContent);
    }

    @Test
    @DisplayName("C-05-09: 응답 메시지 타입이 올바르다")
    public void echoRequest_responseMessageType_isCorrect() throws Exception {
        // Given: 연결 및 인증 완료
        createStageAndConnect();
        authenticate("typeCheckUser");

        TestMessages.EchoRequest echoRequest = new TestMessages.EchoRequest("Type Check", 1);

        // When: Echo 요청
        Packet requestPacket = Packet.builder("EchoRequest")
                .payload(echoRequest.toByteArray())
                .build();

        Packet responsePacket = connector.requestAsync(requestPacket)
                .get(5, TimeUnit.SECONDS);

        // Then: 응답 메시지 타입이 EchoReply여야 함
        assertThat(responsePacket.getMsgId()).isEqualTo("EchoReply");
    }
}
