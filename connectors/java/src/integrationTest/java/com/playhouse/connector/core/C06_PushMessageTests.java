package com.playhouse.connector.core;

import com.playhouse.connector.Packet;
import com.playhouse.connector.support.BaseIntegrationTest;
import com.playhouse.connector.support.TestMessages;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;

import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.atomic.AtomicInteger;

import static org.assertj.core.api.Assertions.assertThat;

/**
 * C-06: Push 메시지 수신 테스트 (BroadcastNotify)
 * <p>
 * 서버에서 클라이언트로 일방적으로 전송하는 Push 메시지를 수신할 수 있는지 검증합니다.
 * </p>
 */
@DisplayName("C-06: Push Message Tests")
public class C06_PushMessageTests extends BaseIntegrationTest {

    @Test
    @DisplayName("C-06-01: Push 메시지를 수신할 수 있다")
    public void onReceive_whenPushMessageSent_receivesMessage() throws Exception {
        createStageAndConnect();
        authenticate("pushUser");

        List<Packet> receivedMessages = new ArrayList<>();
        CompletableFuture<Packet> future = new CompletableFuture<>();

        connector.setOnReceive(packet -> {
            if (packet.getMsgId().equals("BroadcastNotify")) {
                receivedMessages.add(packet);
                future.complete(packet);
            }
        });

        TestMessages.BroadcastRequest broadcastRequest = new TestMessages.BroadcastRequest("Test Broadcast");
        Packet requestPacket = Packet.builder("BroadcastRequest")
                .payload(broadcastRequest.toByteArray())
                .build();
        connector.send(requestPacket);

        boolean completed = waitForCondition(() -> future.isDone(), 5000);

        assertThat(completed).isTrue();
        assertThat(receivedMessages).hasSize(1);
    }

    @Test
    @DisplayName("C-06-02: OnReceive 이벤트가 올바른 파라미터로 호출된다")
    public void onReceive_event_receivesCorrectParameters() throws Exception {
        createStageAndConnect();
        authenticate("paramUser");

        CompletableFuture<String> future = new CompletableFuture<>();

        connector.setOnReceive(packet -> {
            if (packet.getMsgId().equals("BroadcastNotify")) {
                future.complete(packet.getMsgId());
            }
        });

        TestMessages.BroadcastRequest broadcastRequest = new TestMessages.BroadcastRequest("Param Test");
        Packet requestPacket = Packet.builder("BroadcastRequest")
                .payload(broadcastRequest.toByteArray())
                .build();
        connector.send(requestPacket);

        String msgId = waitWithMainThreadAction(future, 5000);
        assertThat(msgId).isEqualTo("BroadcastNotify");
    }

    @Test
    @DisplayName("C-06-03: 여러 개의 Push 메시지를 순차적으로 수신할 수 있다")
    public void onReceive_multiplePushMessages_allReceived() throws Exception {
        createStageAndConnect();
        authenticate("multiUser");

        List<Packet> receivedMessages = new ArrayList<>();
        AtomicInteger expectedCount = new AtomicInteger(3);

        connector.setOnReceive(packet -> {
            if (packet.getMsgId().equals("BroadcastNotify")) {
                receivedMessages.add(packet);
            }
        });

        for (int i = 1; i <= 3; i++) {
            TestMessages.BroadcastRequest request = new TestMessages.BroadcastRequest("Message " + i);
            Packet packet = Packet.builder("BroadcastRequest")
                    .payload(request.toByteArray())
                    .build();
            connector.send(packet);
            Thread.sleep(100);
        }

        boolean completed = waitForCondition(() -> receivedMessages.size() >= expectedCount.get(), 10000);

        assertThat(completed).isTrue();
        assertThat(receivedMessages).hasSizeGreaterThanOrEqualTo(3);
    }

    @Test
    @DisplayName("C-06-04: Push 메시지와 Request-Response를 동시에 처리할 수 있다")
    public void onReceive_pushMessageDuringRequestResponse_bothWork() throws Exception {
        createStageAndConnect();
        authenticate("mixedUser");

        CompletableFuture<Boolean> pushFuture = new CompletableFuture<>();

        connector.setOnReceive(packet -> {
            if (packet.getMsgId().equals("BroadcastNotify")) {
                pushFuture.complete(true);
            }
        });

        // Echo 요청
        CompletableFuture<TestMessages.EchoReply> echoFuture = CompletableFuture.supplyAsync(() -> {
            try {
                return echo("Echo Test", 1);
            } catch (Exception e) {
                throw new RuntimeException(e);
            }
        });

        // Broadcast 요청
        TestMessages.BroadcastRequest broadcastRequest = new TestMessages.BroadcastRequest("Broadcast Test");
        Packet broadcastPacket = Packet.builder("BroadcastRequest")
                .payload(broadcastRequest.toByteArray())
                .build();
        connector.send(broadcastPacket);

        TestMessages.EchoReply echoReply = echoFuture.get();
        Boolean pushReceived = waitWithMainThreadAction(pushFuture, 5000);

        assertThat(echoReply.content).isEqualTo("Echo Test");
        assertThat(pushReceived).isTrue();
    }

    @Test
    @DisplayName("C-06-05: BroadcastNotify에 데이터가 포함된다")
    public void onReceive_broadcastNotify_containsData() throws Exception {
        createStageAndConnect();
        authenticate("dataUser");

        CompletableFuture<TestMessages.BroadcastNotify> future = new CompletableFuture<>();

        connector.setOnReceive(packet -> {
            if (packet.getMsgId().equals("BroadcastNotify")) {
                try {
                    TestMessages.BroadcastNotify notify = TestMessages.BroadcastNotify.parseFrom(packet.getPayload());
                    future.complete(notify);
                } catch (Exception e) {
                    future.completeExceptionally(e);
                }
            }
        });

        TestMessages.BroadcastRequest broadcastRequest = new TestMessages.BroadcastRequest("Data Test");
        Packet requestPacket = Packet.builder("BroadcastRequest")
                .payload(broadcastRequest.toByteArray())
                .build();
        connector.send(requestPacket);

        TestMessages.BroadcastNotify notify = waitWithMainThreadAction(future, 5000);
        assertThat(notify).isNotNull();
        assertThat(notify.eventType).isNotEmpty();
        assertThat(notify.data).isNotEmpty();
    }

    @Test
    @DisplayName("C-06-06: OnReceive 핸들러가 등록되지 않아도 Push 메시지 수신 시 예외가 발생하지 않는다")
    public void onReceive_noHandlerRegistered_noException() throws Exception {
        createStageAndConnect();
        authenticate("noHandlerUser");

        // OnReceive 핸들러를 등록하지 않음
        TestMessages.BroadcastRequest broadcastRequest = new TestMessages.BroadcastRequest("No Handler Test");
        Packet requestPacket = Packet.builder("BroadcastRequest")
                .payload(broadcastRequest.toByteArray())
                .build();

        connector.send(requestPacket);
        Thread.sleep(1000);

        assertThat(connector.isConnected()).isTrue();
    }
}
