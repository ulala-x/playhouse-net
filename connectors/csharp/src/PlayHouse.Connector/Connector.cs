#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PlayHouse.Connector.Internal;
using PlayHouse.Connector.Protocol;

namespace PlayHouse.Connector;

/// <summary>
/// PlayHouse 클라이언트 Connector
/// </summary>
/// <remarks>
/// 클라이언트가 Play Server에 연결하여 실시간 통신을 수행하는 메인 클래스입니다.
/// Connect() 호출 시 지정한 stageId가 모든 Send/Request에서 사용됩니다.
/// </remarks>
public sealed class Connector : IConnectorCallback, IAsyncDisposable
{
    private ClientNetwork? _clientNetwork;
    private bool _disconnectFromClient;
    private long _stageId;
    private string _stageType = string.Empty;

    /// <summary>
    /// Connector 설정
    /// </summary>
    public ConnectorConfig ConnectorConfig { get; private set; } = new();

    /// <summary>
    /// 현재 연결의 Stage ID
    /// </summary>
    public long StageId => _stageId;

    /// <summary>
    /// 현재 연결된 Stage의 타입
    /// </summary>
    public string StageType => _stageType;

    #region Events

    /// <summary>
    /// 연결 결과 이벤트
    /// </summary>
    public event Action<bool>? OnConnect;

    /// <summary>
    /// 메시지 수신 이벤트 (stageId, stageType, packet)
    /// </summary>
    public event Action<long, string, IPacket>? OnReceive;

    /// <summary>
    /// 에러 이벤트 (stageId, stageType, errorCode, request)
    /// </summary>
    public event Action<long, string, ushort, IPacket>? OnError;

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
        OnReceive?.Invoke(stageId, _stageType, packet);
    }

    void IConnectorCallback.ErrorCallback(long stageId, ushort errorCode, IPacket request)
    {
        OnError?.Invoke(stageId, _stageType, errorCode, request);
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
    /// <param name="host">서버 호스트 주소</param>
    /// <param name="port">서버 포트</param>
    /// <param name="stageId">Stage ID (모든 Send/Request에서 사용)</param>
    /// <param name="stageType">Stage 타입</param>
    /// <param name="debugMode">디버그 모드</param>
    public void Connect(string host, int port, long stageId, string stageType, bool debugMode = false)
    {
        _disconnectFromClient = false;
        _stageId = stageId;
        _stageType = stageType;
        _clientNetwork!.Connect(host, port, debugMode);
    }

    /// <summary>
    /// 서버에 비동기 연결
    /// </summary>
    /// <param name="host">서버 호스트 주소</param>
    /// <param name="port">서버 포트</param>
    /// <param name="stageId">Stage ID (모든 Send/Request에서 사용)</param>
    /// <param name="stageType">Stage 타입</param>
    /// <param name="debugMode">디버그 모드</param>
    /// <returns>연결 성공 여부</returns>
    public async Task<bool> ConnectAsync(string host, int port, long stageId, string stageType, bool debugMode = false)
    {
        _disconnectFromClient = false;
        _stageId = stageId;
        _stageType = stageType;
        return await _clientNetwork!.ConnectAsync(host, port, debugMode);
    }

    /// <summary>
    /// 서버 연결 끊기 (비동기)
    /// </summary>
    public async Task DisconnectAsync()
    {
        _disconnectFromClient = true;
        if (_clientNetwork != null)
        {
            await _clientNetwork.DisconnectAsync();
        }
    }

    /// <summary>
    /// 서버 연결 끊기 (동기)
    /// </summary>
    public void Disconnect()
    {
        _disconnectFromClient = true;
        _clientNetwork?.DisconnectAsync().GetAwaiter().GetResult();
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
    /// 인증 요청 (콜백 방식)/
    /// </summary>
    /// <param name="request">인증 요청 패킷</param>
    /// <param name="callback">응답 콜백</param>
    public void Authenticate(IPacket request, Action<IPacket> callback)
    {
        if (!IsConnected())
        {
            OnError?.Invoke(_stageId, _stageType, (ushort)ConnectorErrorCode.Disconnected, request);
            return;
        }

        _clientNetwork!.Request(request, callback, _stageId, isAuthenticate: true);
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
            throw new ConnectorException(_stageId, (ushort)ConnectorErrorCode.Disconnected, request, 0);
        }

        return await _clientNetwork!.RequestAsync(request, _stageId, isAuthenticate: true);
    }

    #endregion

    #region Send/Request

    /// <summary>
    /// 메시지 전송 (응답 없음)
    /// </summary>
    /// <param name="packet">전송할 패킷</param>
    public void Send(IPacket packet)
    {
        if (!IsConnected())
        {
            OnError?.Invoke(_stageId, _stageType, (ushort)ConnectorErrorCode.Disconnected, packet);
            return;
        }

        _clientNetwork!.Send(packet, _stageId);
    }

    /// <summary>
    /// 요청 전송 (콜백 방식)
    /// </summary>
    /// <param name="request">요청 패킷</param>
    /// <param name="callback">응답 콜백 - 패킷은 콜백 종료 후 자동으로 dispose됨</param>
    /// <remarks>
    /// 콜백으로 전달된 IPacket은 콜백 실행이 끝나면 자동으로 dispose됩니다.
    /// 콜백 외부에서 사용하려면 데이터를 복사해야 합니다.
    /// </remarks>
    public void Request(IPacket request, Action<IPacket> callback)
    {
        if (!IsConnected())
        {
            OnError?.Invoke(_stageId, _stageType, (ushort)ConnectorErrorCode.Disconnected, request);
            return;
        }

        _clientNetwork!.Request(request, callback, _stageId);
    }

    /// <summary>
    /// 요청 전송 (async/await 방식)
    /// </summary>
    /// <param name="request">요청 패킷</param>
    /// <returns>응답 패킷 - 호출자가 반드시 Dispose() 해야 함</returns>
    /// <remarks>
    /// 반환된 IPacket은 호출자가 소유하며, 사용 후 반드시 Dispose()를 호출하거나
    /// using 문을 사용해야 합니다.
    /// </remarks>
    public async Task<IPacket> RequestAsync(IPacket request)
    {
        if (!IsConnected())
        {
            throw new ConnectorException(_stageId, (ushort)ConnectorErrorCode.Disconnected, request, 0);
        }

        return await _clientNetwork!.RequestAsync(request, _stageId);
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

    #region IAsyncDisposable

    /// <summary>
    /// 비동기 리소스 정리
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_clientNetwork != null)
        {
            await _clientNetwork.DisposeAsync();
            _clientNetwork = null;
        }
    }

    #endregion

}
