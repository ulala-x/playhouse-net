#nullable enable

using PlayHouse.Connector.Internal;
using PlayHouse.Connector.Protocol;

namespace PlayHouse.Connector;

/// <summary>
/// PlayHouse 클라이언트 Connector
/// </summary>
/// <remarks>
/// 클라이언트가 Play Server에 연결하여 실시간 통신을 수행하는 메인 클래스입니다.
/// serviceId 파라미터가 제거되어 하나의 연결에 하나의 서비스만 사용합니다.
/// </remarks>
public sealed class Connector : IConnectorCallback
{
    private ClientNetwork? _clientNetwork;
    private bool _disconnectFromClient;

    /// <summary>
    /// Connector 설정
    /// </summary>
    public ConnectorConfig ConnectorConfig { get; private set; } = new();

    #region Events

    /// <summary>
    /// 연결 결과 이벤트
    /// </summary>
    public event Action<bool>? OnConnect;

    /// <summary>
    /// 메시지 수신 이벤트 (stageId, packet)
    /// stageId가 0이면 Stage 없는 메시지
    /// </summary>
    public event Action<long, IPacket>? OnReceive;

    /// <summary>
    /// 에러 이벤트 (stageId, errorCode, request)
    /// </summary>
    public event Action<long, ushort, IPacket>? OnError;

    /// <summary>
    /// 연결 끊김 이벤트
    /// </summary>
    public event Action? OnDisconnect;

    #endregion

    #region IConnectorCallback Implementation

    void IConnectorCallback.ConnectCallback(bool result)
    {
        OnConnect?.Invoke(result);
    }

    void IConnectorCallback.ReceiveCallback(long stageId, IPacket packet)
    {
        OnReceive?.Invoke(stageId, packet);
    }

    void IConnectorCallback.ErrorCallback(long stageId, ushort errorCode, IPacket request)
    {
        OnError?.Invoke(stageId, errorCode, request);
    }

    void IConnectorCallback.DisconnectCallback()
    {
        if (_disconnectFromClient)
        {
            return;
        }

        OnDisconnect?.Invoke();
    }

    #endregion

    #region Initialization

    /// <summary>
    /// Connector 초기화
    /// </summary>
    /// <param name="config">설정</param>
    public void Init(ConnectorConfig config)
    {
        ConnectorConfig = config ?? throw new ArgumentNullException(nameof(config));
        _clientNetwork = new ClientNetwork(config, this);
    }

    #endregion

    #region Connection

    /// <summary>
    /// 서버에 연결 (비동기로 OnConnect 이벤트 발생)
    /// </summary>
    /// <param name="debugMode">디버그 모드</param>
    public void Connect(bool debugMode = false)
    {
        _disconnectFromClient = false;
        _clientNetwork!.Connect(debugMode);
    }

    /// <summary>
    /// 서버에 비동기 연결
    /// </summary>
    /// <param name="debugMode">디버그 모드</param>
    /// <returns>연결 성공 여부</returns>
    public async Task<bool> ConnectAsync(bool debugMode = false)
    {
        _disconnectFromClient = false;
        return await _clientNetwork!.ConnectAsync(debugMode);
    }

    /// <summary>
    /// 서버 연결 끊기
    /// </summary>
    public void Disconnect()
    {
        _disconnectFromClient = true;
        _ = _clientNetwork?.DisconnectAsync();
    }

    /// <summary>
    /// 연결 상태 확인
    /// </summary>
    public bool IsConnected()
    {
        return _clientNetwork?.IsConnect() ?? false;
    }

    /// <summary>
    /// 인증 상태 확인
    /// </summary>
    public bool IsAuthenticated()
    {
        return _clientNetwork?.IsAuthenticated() ?? false;
    }

    #endregion

    #region Authentication

    /// <summary>
    /// 인증 메시지 ID 설정
    /// 등록된 메시지만 인증 전에 전송 가능
    /// </summary>
    /// <param name="msgId">인증 메시지 ID</param>
    public void SetAuthenticateMessageId(string msgId)
    {
        _clientNetwork?.SetAuthenticateMessageId(msgId);
    }

    /// <summary>
    /// 인증 요청 (콜백 방식)
    /// </summary>
    /// <param name="request">인증 요청 패킷</param>
    /// <param name="callback">응답 콜백</param>
    public void Authenticate(IPacket request, Action<IPacket> callback)
    {
        if (!IsConnected())
        {
            OnError?.Invoke(0, (ushort)ConnectorErrorCode.Disconnected, request);
            return;
        }

        _clientNetwork!.Request(request, callback, 0, isAuthenticate: true);
    }

    /// <summary>
    /// 인증 요청 (async/await 방식)
    /// </summary>
    /// <param name="request">인증 요청 패킷</param>
    /// <returns>인증 응답 패킷</returns>
    public async Task<IPacket> AuthenticateAsync(IPacket request)
    {
        if (!IsConnected())
        {
            throw new ConnectorException(0, (ushort)ConnectorErrorCode.Disconnected, request, 0);
        }

        return await _clientNetwork!.RequestAsync(request, 0, isAuthenticate: true);
    }

    #endregion

    #region Send/Request (No Stage)

    /// <summary>
    /// 메시지 전송 (Stage 없음, 응답 없음)
    /// </summary>
    /// <param name="packet">전송할 패킷</param>
    public void Send(IPacket packet)
    {
        if (!IsConnected())
        {
            OnError?.Invoke(0, (ushort)ConnectorErrorCode.Disconnected, packet);
            return;
        }

        if (!IsAuthenticated())
        {
            OnError?.Invoke(0, (ushort)ConnectorErrorCode.Unauthenticated, packet);
            return;
        }

        _clientNetwork!.Send(packet, 0);
    }

    /// <summary>
    /// 요청 전송 (Stage 없음, 콜백 방식)
    /// </summary>
    /// <param name="request">요청 패킷</param>
    /// <param name="callback">응답 콜백</param>
    public void Request(IPacket request, Action<IPacket> callback)
    {
        if (!IsConnected())
        {
            OnError?.Invoke(0, (ushort)ConnectorErrorCode.Disconnected, request);
            return;
        }

        _clientNetwork!.Request(request, callback, 0);
    }

    /// <summary>
    /// 요청 전송 (Stage 없음, async/await 방식)
    /// </summary>
    /// <param name="request">요청 패킷</param>
    /// <returns>응답 패킷</returns>
    public async Task<IPacket> RequestAsync(IPacket request)
    {
        if (!IsConnected())
        {
            throw new ConnectorException(0, (ushort)ConnectorErrorCode.Disconnected, request, 0);
        }

        return await _clientNetwork!.RequestAsync(request, 0);
    }

    #endregion

    #region Send/Request (With Stage)

    /// <summary>
    /// 메시지 전송 (Stage 지정, 응답 없음)
    /// </summary>
    /// <param name="stageId">Stage ID</param>
    /// <param name="packet">전송할 패킷</param>
    public void Send(long stageId, IPacket packet)
    {
        if (!IsConnected())
        {
            OnError?.Invoke(stageId, (ushort)ConnectorErrorCode.Disconnected, packet);
            return;
        }

        _clientNetwork!.Send(packet, stageId);
    }

    /// <summary>
    /// 요청 전송 (Stage 지정, 콜백 방식)
    /// </summary>
    /// <param name="stageId">Stage ID</param>
    /// <param name="request">요청 패킷</param>
    /// <param name="callback">응답 콜백</param>
    public void Request(long stageId, IPacket request, Action<IPacket> callback)
    {
        if (!IsConnected())
        {
            OnError?.Invoke(stageId, (ushort)ConnectorErrorCode.Disconnected, request);
            return;
        }

        _clientNetwork!.Request(request, callback, stageId);
    }

    /// <summary>
    /// 요청 전송 (Stage 지정, async/await 방식)
    /// </summary>
    /// <param name="stageId">Stage ID</param>
    /// <param name="request">요청 패킷</param>
    /// <returns>응답 패킷</returns>
    public async Task<IPacket> RequestAsync(long stageId, IPacket request)
    {
        if (!IsConnected())
        {
            throw new ConnectorException(stageId, (ushort)ConnectorErrorCode.Disconnected, request, 0);
        }

        return await _clientNetwork!.RequestAsync(request, stageId);
    }

    #endregion

    #region Unity Support

    /// <summary>
    /// 메인 스레드에서 콜백 실행 (Unity Update에서 호출)
    /// </summary>
    public void MainThreadAction()
    {
        _clientNetwork?.MainThreadAction();
    }

    #endregion

    #region Cache

    /// <summary>
    /// 캐시 정리
    /// </summary>
    public void ClearCache()
    {
        _clientNetwork?.ClearCache();
    }

    #endregion
}
