package com.playhouse.connector.support;

import com.google.gson.Gson;
import com.google.gson.GsonBuilder;
import okhttp3.*;

import java.io.IOException;
import java.util.HashMap;
import java.util.Map;
import java.util.concurrent.atomic.AtomicLong;

/**
 * 테스트 서버 HTTP API 클라이언트
 * <p>
 * Stage 생성 등의 테스트 인프라 작업을 위한 HTTP 클라이언트
 * </p>
 */
public class TestServerClient {
    private static final MediaType JSON = MediaType.get("application/json; charset=utf-8");

    private final OkHttpClient httpClient;
    private final Gson gson;
    private final String baseUrl;
    private final AtomicLong stageIdCounter = new AtomicLong(1);

    /**
     * 환경 변수에서 설정을 읽어 초기화
     */
    public TestServerClient() {
        this(
                System.getenv().getOrDefault("TEST_SERVER_HOST", "localhost"),
                Integer.parseInt(System.getenv().getOrDefault("TEST_SERVER_HTTP_PORT", "28080"))
        );
    }

    /**
     * 호스트와 포트를 지정하여 초기화
     *
     * @param host     테스트 서버 호스트
     * @param httpPort HTTP 포트
     */
    public TestServerClient(String host, int httpPort) {
        this.httpClient = new OkHttpClient.Builder()
                .build();

        this.gson = new GsonBuilder()
                .setLenient()
                .create();

        this.baseUrl = String.format("http://%s:%d", host, httpPort);
    }

    /**
     * 테스트용 Stage 생성
     *
     * @param stageType Stage 타입
     * @return 생성된 Stage 정보
     * @throws IOException HTTP 요청 실패 시
     */
    public CreateStageResponse createStage(String stageType) throws IOException {
        return createStage(stageType, null);
    }

    /**
     * 테스트용 Stage 생성
     *
     * @param stageType  Stage 타입
     * @param maxPlayers 최대 플레이어 수 (선택 사항)
     * @return 생성된 Stage 정보
     * @throws IOException HTTP 요청 실패 시
     */
    public CreateStageResponse createStage(String stageType, Integer maxPlayers) throws IOException {
        long stageId = stageIdCounter.getAndIncrement();

        Map<String, Object> requestBody = new HashMap<>();
        requestBody.put("stageType", stageType);
        requestBody.put("stageId", stageId);
        if (maxPlayers != null) {
            requestBody.put("maxPlayers", maxPlayers);
        }

        String jsonBody = gson.toJson(requestBody);
        RequestBody body = RequestBody.create(jsonBody, JSON);

        Request request = new Request.Builder()
                .url(baseUrl + "/api/stages/get-or-create")
                .post(body)
                .build();

        try (Response response = httpClient.newCall(request).execute()) {
            if (!response.isSuccessful()) {
                throw new IOException("Failed to create stage: " + response);
            }

            String responseBody = response.body().string();
            ApiResponse apiResponse = gson.fromJson(responseBody, ApiResponse.class);

            return new CreateStageResponse(
                    apiResponse.success,
                    apiResponse.stageId,
                    stageType,
                    apiResponse.replyPayloadId
            );
        }
    }

    /**
     * 기본 TestStage 타입으로 Stage 생성
     *
     * @return 생성된 Stage 정보
     * @throws IOException HTTP 요청 실패 시
     */
    public CreateStageResponse createTestStage() throws IOException {
        return createStage("TestStage");
    }

    /**
     * HTTP 클라이언트 종료
     */
    public void close() {
        httpClient.dispatcher().executorService().shutdown();
        httpClient.connectionPool().evictAll();
    }

    /**
     * API 응답 매핑용 내부 클래스
     */
    private static class ApiResponse {
        boolean success;
        boolean isCreated;
        long stageId;
        String replyPayloadId;
    }
}
