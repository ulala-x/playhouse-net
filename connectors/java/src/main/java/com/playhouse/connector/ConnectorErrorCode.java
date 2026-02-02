package com.playhouse.connector;

/**
 * Connector 에러 코드
 * <p>
 * C#, JavaScript connector와 동일한 에러 코드 체계 사용
 */
public enum ConnectorErrorCode {

    /**
     * 연결이 끊어짐
     */
    DISCONNECTED(60201, "Connection is disconnected"),

    /**
     * 요청 타임아웃
     */
    REQUEST_TIMEOUT(60202, "Request timeout"),

    /**
     * 인증되지 않음
     */
    UNAUTHENTICATED(60203, "Not authenticated");

    private final int code;
    private final String message;

    /**
     * ConnectorErrorCode 생성자
     *
     * @param code    에러 코드
     * @param message 에러 메시지
     */
    ConnectorErrorCode(int code, String message) {
        this.code = code;
        this.message = message;
    }

    /**
     * 에러 코드 반환
     *
     * @return 에러 코드
     */
    public int getCode() {
        return code;
    }

    /**
     * 에러 메시지 반환
     *
     * @return 에러 메시지
     */
    public String getMessage() {
        return message;
    }

    /**
     * 에러 코드로부터 ConnectorErrorCode 찾기
     *
     * @param code 에러 코드
     * @return ConnectorErrorCode 또는 null
     */
    public static ConnectorErrorCode fromCode(int code) {
        for (ConnectorErrorCode errorCode : values()) {
            if (errorCode.code == code) {
                return errorCode;
            }
        }
        return null;
    }

    @Override
    public String toString() {
        return String.format("ConnectorErrorCode{code=%d, message='%s'}", code, message);
    }
}
