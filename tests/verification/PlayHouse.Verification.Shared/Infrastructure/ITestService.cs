#nullable enable

namespace PlayHouse.Verification.Shared.Infrastructure;

/// <summary>
/// DI 테스트용 사용자 정의 서비스 인터페이스.
/// Stage/Actor에 주입되어 DI 통합을 검증합니다.
/// </summary>
public interface ITestService
{
    string GetValue();
}

/// <summary>
/// ITestService 구현체.
/// DI 컨테이너에 등록되어 Stage/Actor에 주입됩니다.
/// </summary>
public class TestService : ITestService
{
    public string GetValue() => "DI-Injected-Value";
}
