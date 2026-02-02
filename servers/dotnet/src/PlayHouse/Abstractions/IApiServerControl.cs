#nullable enable

namespace PlayHouse.Abstractions;

/// <summary>
/// ApiServer 제어 인터페이스.
/// </summary>
/// <remarks>
/// 외부에서 ApiServer를 중지하거나 제어하기 위한 인터페이스입니다.
/// ASP.NET Core DI 컨테이너에 등록하여 사용할 수 있습니다.
/// </remarks>
public interface IApiServerControl
{
    /// <summary>
    /// Gets or sets the diagnostic level.
    /// -1: Normal, 0: Raw Echo, 1: Header Parse Echo
    /// </summary>
    int DiagnosticLevel { get; set; }

    /// <summary>
    /// ApiServer를 중지합니다.
    /// </summary>
    /// <param name="cancellationToken">취소 토큰.</param>
    /// <returns>중지 완료를 나타내는 Task.</returns>
    Task StopAsync(CancellationToken cancellationToken = default);
}
