package com.playhouse.connector;

/**
 * Connector 예외
 */
public class ConnectorException extends RuntimeException {

    private final int errorCode;
    private final Packet request;

    /**
     * ConnectorException 생성자
     *
     * @param errorCode 에러 코드
     * @param message   에러 메시지
     */
    public ConnectorException(int errorCode, String message) {
        super(message);
        this.errorCode = errorCode;
        this.request = null;
    }

    /**
     * ConnectorException 생성자
     *
     * @param errorCode 에러 코드
     * @param message   에러 메시지
     * @param cause     원인 예외
     */
    public ConnectorException(int errorCode, String message, Throwable cause) {
        super(message, cause);
        this.errorCode = errorCode;
        this.request = null;
    }

    /**
     * ConnectorException 생성자
     *
     * @param errorCode 에러 코드
     * @param message   에러 메시지
     * @param request   실패한 요청 패킷
     */
    public ConnectorException(int errorCode, String message, Packet request) {
        super(message);
        this.errorCode = errorCode;
        this.request = request;
    }

    /**
     * 에러 코드 반환
     *
     * @return 에러 코드
     */
    public int getErrorCode() {
        return errorCode;
    }

    /**
     * 실패한 요청 패킷 반환
     *
     * @return 요청 패킷 (없으면 null)
     */
    public Packet getRequest() {
        return request;
    }

    @Override
    public String toString() {
        if (request != null) {
            return String.format("ConnectorException{errorCode=%d, message='%s', request=%s}",
                errorCode, getMessage(), request);
        }
        return String.format("ConnectorException{errorCode=%d, message='%s'}",
            errorCode, getMessage());
    }
}
