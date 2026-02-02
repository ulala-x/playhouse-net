package com.playhouse.connector.core;

import com.playhouse.connector.support.BaseIntegrationTest;
import com.playhouse.connector.support.CreateStageResponse;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;

import static org.assertj.core.api.Assertions.assertThat;

/**
 * C-01: Stage 생성 테스트
 * <p>
 * HTTP API를 통해 테스트 서버에 Stage를 생성할 수 있는지 검증합니다.
 * </p>
 */
@DisplayName("C-01: Stage Creation Tests")
public class C01_StageCreationTests extends BaseIntegrationTest {

    @Override
    @BeforeEach
    public void setUp() throws Exception {
        // 이 테스트는 Connector 초기화가 필요 없음 (HTTP API만 테스트)
        host = System.getenv().getOrDefault("TEST_SERVER_HOST", "localhost");
        httpPort = Integer.parseInt(System.getenv().getOrDefault("TEST_SERVER_HTTP_PORT", "28080"));
        tcpPort = Integer.parseInt(System.getenv().getOrDefault("TEST_SERVER_TCP_PORT", "28001"));

        testServer = new com.playhouse.connector.support.TestServerClient(host, httpPort);
    }

    @Override
    @AfterEach
    public void tearDown() throws Exception {
        if (testServer != null) {
            testServer.close();
            testServer = null;
        }
    }

    @Test
    @DisplayName("C-01-01: TestStage 타입으로 Stage를 생성할 수 있다")
    public void createStage_withTestStageType_returnsValidStageInfo() throws Exception {
        // When: TestStage 타입으로 Stage 생성
        CreateStageResponse stageInfo = testServer.createTestStage();

        // Then: Stage 정보가 올바르게 반환되어야 함
        assertThat(stageInfo).isNotNull();
        assertThat(stageInfo.isSuccess()).isTrue();
        assertThat(stageInfo.getStageId()).isGreaterThan(0);
        assertThat(stageInfo.getStageType()).isEqualTo("TestStage");
        // replyPayloadId는 새로 생성된 Stage에만 반환됨
        if (stageInfo.isCreated()) {
            assertThat(stageInfo.getReplyPayloadId()).isNotNull();
        }
    }

    @Test
    @DisplayName("C-01-02: 커스텀 페이로드로 Stage를 생성할 수 있다")
    public void createStage_withCustomPayload_returnsValidStageInfo() throws Exception {
        // When: 최대 플레이어 수를 지정하여 Stage 생성
        CreateStageResponse stageInfo = testServer.createStage("TestStage", 10);

        // Then: Stage가 성공적으로 생성되어야 함
        assertThat(stageInfo).isNotNull();
        assertThat(stageInfo.getStageId()).isGreaterThan(0);
        assertThat(stageInfo.getStageType()).isEqualTo("TestStage");
    }

    @Test
    @DisplayName("C-01-03: 여러 개의 Stage를 생성할 수 있다")
    public void createStage_multipleTimes_returnsUniqueStageIds() throws Exception {
        // When: 3개의 Stage 생성
        CreateStageResponse stage1 = testServer.createTestStage();
        CreateStageResponse stage2 = testServer.createTestStage();
        CreateStageResponse stage3 = testServer.createTestStage();

        // Then: 각 Stage는 고유한 ID를 가져야 함
        assertThat(stage1.getStageId()).isNotEqualTo(stage2.getStageId());
        assertThat(stage2.getStageId()).isNotEqualTo(stage3.getStageId());
        assertThat(stage1.getStageId()).isNotEqualTo(stage3.getStageId());

        // 모든 Stage 타입은 동일해야 함
        assertThat(stage1.getStageType()).isEqualTo("TestStage");
        assertThat(stage2.getStageType()).isEqualTo("TestStage");
        assertThat(stage3.getStageType()).isEqualTo("TestStage");
    }
}
