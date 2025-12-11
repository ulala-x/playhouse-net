#nullable enable

using PlayHouse.Abstractions.Play;

namespace PlayHouse.Bootstrap;

/// <summary>
/// Play Server 부트스트랩 빌더.
/// </summary>
/// <remarks>
/// 사용 예시:
/// <code>
/// var playServer = new PlayServerBootstrap()
///     .Configure(options =>
///     {
///         options.ServiceId = 1;
///         options.ServerId = 1;
///         options.BindEndpoint = "tcp://0.0.0.0:5000";
///         options.ClientEndpoint = "tcp://0.0.0.0:6000";
///     })
///     .UseStage&lt;GameRoomStage&gt;("GameRoom")
///     .UseActor&lt;PlayerActor&gt;()
///     .Build();
///
/// await playServer.StartAsync();
/// </code>
/// </remarks>
public sealed class PlayServerBootstrap
{
    private readonly PlayServerOption _options = new();
    private readonly Dictionary<string, Type> _stageTypes = new();
    private Type? _actorType;

    /// <summary>
    /// 서버 옵션을 설정합니다.
    /// </summary>
    /// <param name="configure">설정 액션.</param>
    /// <returns>빌더 인스턴스.</returns>
    public PlayServerBootstrap Configure(Action<PlayServerOption> configure)
    {
        configure(_options);
        return this;
    }

    /// <summary>
    /// Stage 타입을 등록합니다.
    /// </summary>
    /// <typeparam name="TStage">IStage 구현 타입.</typeparam>
    /// <param name="stageType">Stage 타입 이름.</param>
    /// <returns>빌더 인스턴스.</returns>
    public PlayServerBootstrap UseStage<TStage>(string stageType) where TStage : class, IStage, new()
    {
        _stageTypes[stageType] = typeof(TStage);
        return this;
    }

    /// <summary>
    /// Actor 타입을 등록합니다.
    /// </summary>
    /// <typeparam name="TActor">IActor 구현 타입.</typeparam>
    /// <returns>빌더 인스턴스.</returns>
    public PlayServerBootstrap UseActor<TActor>() where TActor : class, IActor, new()
    {
        _actorType = typeof(TActor);
        return this;
    }

    /// <summary>
    /// Play Server 인스턴스를 생성합니다.
    /// </summary>
    /// <returns>PlayServer 인스턴스.</returns>
    public PlayServer Build()
    {
        _options.Validate();

        if (_stageTypes.Count == 0)
            throw new InvalidOperationException("At least one Stage type must be registered. Use UseStage<T>().");

        if (_actorType == null)
            throw new InvalidOperationException("Actor type must be registered. Use UseActor<T>().");

        var producer = new PlayProducer(_stageTypes, _actorType);
        return new PlayServer(_options, producer);
    }
}
