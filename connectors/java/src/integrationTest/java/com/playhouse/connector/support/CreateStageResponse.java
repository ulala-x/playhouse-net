package com.playhouse.connector.support;

/**
 * Test server Stage 생성 응답
 */
public class CreateStageResponse {
    private boolean success;
    private long stageId;
    private String stageType;
    private String replyPayloadId;

    public CreateStageResponse() {
    }

    public CreateStageResponse(boolean success, long stageId, String stageType, String replyPayloadId) {
        this.success = success;
        this.stageId = stageId;
        this.stageType = stageType;
        this.replyPayloadId = replyPayloadId;
    }

    public boolean isSuccess() {
        return success;
    }

    public void setSuccess(boolean success) {
        this.success = success;
    }

    public long getStageId() {
        return stageId;
    }

    public void setStageId(long stageId) {
        this.stageId = stageId;
    }

    public String getStageType() {
        return stageType;
    }

    public void setStageType(String stageType) {
        this.stageType = stageType;
    }

    public String getReplyPayloadId() {
        return replyPayloadId;
    }

    public void setReplyPayloadId(String replyPayloadId) {
        this.replyPayloadId = replyPayloadId;
    }
}
