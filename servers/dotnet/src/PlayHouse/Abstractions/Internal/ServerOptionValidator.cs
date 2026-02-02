#nullable enable

namespace PlayHouse.Abstractions.Internal;

internal static class ServerOptionValidator
{
    internal static void ValidateIdentity(ServerType serverType, string serverId, string bindEndpoint)
    {
        if (serverType == 0)
            throw new InvalidOperationException("ServerType must be set");

        if (string.IsNullOrEmpty(serverId))
            throw new InvalidOperationException("ServerId is required");

        if (string.IsNullOrEmpty(bindEndpoint))
            throw new InvalidOperationException("BindEndpoint is required");
    }

    internal static void ValidateRequestTimeout(int requestTimeoutMs)
    {
        if (requestTimeoutMs <= 0)
            throw new InvalidOperationException("RequestTimeoutMs must be greater than 0.");
    }
}
