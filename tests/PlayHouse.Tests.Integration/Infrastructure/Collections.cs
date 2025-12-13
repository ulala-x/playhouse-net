#nullable enable

using PlayHouse.Tests.Integration.Infrastructure.Fixtures;
using Xunit;

namespace PlayHouse.Tests.Integration.Infrastructure;

/// <summary>
/// xUnit Collection 정의.
/// Collection Fixture를 사용하여 테스트 클래스 간 서버 인스턴스를 공유합니다.
/// </summary>

[CollectionDefinition("E2E Single PlayServer")]
public class SinglePlayServerCollection : ICollectionFixture<SinglePlayServerFixture>
{
    // 이 클래스는 마커 역할만 하며 코드를 포함하지 않습니다.
}

[CollectionDefinition("E2E Dual PlayServer")]
public class DualPlayServerCollection : ICollectionFixture<DualPlayServerFixture>
{
    // 이 클래스는 마커 역할만 하며 코드를 포함하지 않습니다.
}

[CollectionDefinition("E2E ApiPlayServer")]
public class ApiPlayServerCollection : ICollectionFixture<ApiPlayServerFixture>
{
    // 이 클래스는 마커 역할만 하며 코드를 포함하지 않습니다.
}

[CollectionDefinition("E2E Dual ApiServer")]
public class DualApiServerCollection : ICollectionFixture<DualApiServerFixture>
{
    // 이 클래스는 마커 역할만 하며 코드를 포함하지 않습니다.
}

[CollectionDefinition("E2E Single ApiServer")]
public class SingleApiServerCollection : ICollectionFixture<SingleApiServerFixture>
{
    // 이 클래스는 마커 역할만 하며 코드를 포함하지 않습니다.
}

/// <summary>
/// Connector 테스트용 Collection.
/// ConnectionTests, MessagingTests, PushTests, ActorCallbackTests가 SinglePlayServerFixture를 공유합니다.
/// </summary>
[CollectionDefinition("E2E Connector Tests")]
public class ConnectorTestsCollection : ICollectionFixture<SinglePlayServerFixture>
{
    // 이 클래스는 마커 역할만 하며 코드를 포함하지 않습니다.
}

/// <summary>
/// 서버 생명주기 테스트용 Collection.
/// 서버 종료 테스트는 다른 테스트에 영향을 주므로 별도 Collection에서 자체 서버로 실행합니다.
/// </summary>
[CollectionDefinition("E2E Server Lifecycle Tests")]
public class ServerLifecycleCollection
{
    // 이 클래스는 마커 역할만 하며 코드를 포함하지 않습니다.
    // 이 Collection은 Fixture를 사용하지 않으며, 각 테스트가 독립적인 서버를 생성합니다.
}

/// <summary>
/// ISender 테스트용 Collection (Stage간 통신).
/// 두 개의 PlayServer를 구동하므로 각 테스트가 독립적인 서버를 생성합니다.
/// </summary>
[CollectionDefinition("E2E ISender Tests")]
public class ISenderTestsCollection
{
    // 이 클래스는 마커 역할만 하며 코드를 포함하지 않습니다.
}

/// <summary>
/// IApiSender 테스트용 Collection (Api → Play 통신).
/// PlayServer와 ApiServer를 구동하므로 각 테스트가 독립적인 서버를 생성합니다.
/// </summary>
[CollectionDefinition("E2E IApiSender Tests")]
public class IApiSenderTestsCollection
{
    // 이 클래스는 마커 역할만 하며 코드를 포함하지 않습니다.
}

/// <summary>
/// ApiToApi 테스트용 Collection (Api 간 통신).
/// 두 개의 ApiServer를 구동하므로 각 테스트가 독립적인 서버를 생성합니다.
/// </summary>
[CollectionDefinition("E2E ApiToApi Tests")]
public class ApiToApiTestsCollection
{
    // 이 클래스는 마커 역할만 하며 코드를 포함하지 않습니다.
}

/// <summary>
/// SelfConnection 테스트용 Collection (자기 자신에게 메시지).
/// 하나의 ApiServer를 구동하므로 각 테스트가 독립적인 서버를 생성합니다.
/// </summary>
[CollectionDefinition("E2E SelfConnection Tests")]
public class SelfConnectionTestsCollection
{
    // 이 클래스는 마커 역할만 하며 코드를 포함하지 않습니다.
}
