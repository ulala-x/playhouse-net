#nullable enable

using PlayHouse.Abstractions.Api;

namespace PlayHouse.Bootstrap;

/// <summary>
/// API Server 부트스트랩 빌더.
/// </summary>
/// <remarks>
/// 사용 예시:
/// <code>
/// var apiServer = new ApiServerBootstrap()
///     .Configure(options =>
///     {
///         options.ServiceId = 2;
///         options.ServerId = 1;
///         options.BindEndpoint = "tcp://0.0.0.0:5100";
///     })
///     .UseController&lt;GameApiController&gt;()
///     .Build();
///
/// await apiServer.StartAsync();
/// </code>
/// </remarks>
public sealed class ApiServerBootstrap
{
    private readonly ApiServerOption _options = new();
    private readonly List<Type> _controllerTypes = new();

    /// <summary>
    /// 서버 옵션을 설정합니다.
    /// </summary>
    /// <param name="configure">설정 액션.</param>
    /// <returns>빌더 인스턴스.</returns>
    public ApiServerBootstrap Configure(Action<ApiServerOption> configure)
    {
        configure(_options);
        return this;
    }

    /// <summary>
    /// API Controller 타입을 등록합니다.
    /// </summary>
    /// <typeparam name="TController">IApiController 구현 타입.</typeparam>
    /// <returns>빌더 인스턴스.</returns>
    public ApiServerBootstrap UseController<TController>() where TController : class, IApiController
    {
        _controllerTypes.Add(typeof(TController));
        return this;
    }

    /// <summary>
    /// API Server 인스턴스를 생성합니다.
    /// </summary>
    /// <returns>ApiServer 인스턴스.</returns>
    public ApiServer Build()
    {
        _options.Validate();

        return new ApiServer(_options, _controllerTypes);
    }
}
