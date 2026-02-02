package com.playhouse.connector.callback;

import com.playhouse.connector.Packet;

/**
 * Connector 콜백 인터페이스
 */
public interface ConnectorCallback {

    /**
     * 연결 콜백
     *
     * @param success 연결 성공 여부
     */
    void onConnect(boolean success);

    /**
     * 메시지 수신 콜백
     *
     * @param packet 수신된 패킷
     */
    void onReceive(Packet packet);

    /**
     * 에러 콜백
     *
     * @param errorCode 에러 코드
     * @param message   에러 메시지
     */
    void onError(int errorCode, String message);

    /**
     * 연결 끊김 콜백
     */
    void onDisconnect();
}
