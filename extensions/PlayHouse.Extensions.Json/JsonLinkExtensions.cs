#nullable enable

using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Api;
using PlayHouse.Abstractions.Play;
using PlayHouse.Runtime.ServerMesh.Discovery;

namespace PlayHouse.Extensions.Json;

/// <summary>
/// Extension methods for ILink and its derived interfaces to support JSON messages.
/// </summary>
public static class JsonLinkExtensions
{
    #region ILink - Reply

    /// <summary>
    /// Sends a JSON object as a reply to the current request.
    /// </summary>
    /// <typeparam name="T">The JSON-serializable object type.</typeparam>
    /// <param name="link">The sender instance.</param>
    /// <param name="obj">The object to send as a reply.</param>
    /// <example>
    /// <code>
    /// public Task OnDispatch(IPacket packet, IStageSender sender)
    /// {
    ///     sender.Reply(new ChatResponse { Message = "Hello" });
    ///     return Task.CompletedTask;
    /// }
    /// </code>
    /// </example>
    public static void Reply<T>(this ILink link, T obj) where T : class
    {
        link.Reply(JsonCPacketExtensions.Of(obj));
    }

    #endregion

    #region ILink - API Server Communication

    /// <summary>
    /// Sends a one-way JSON message to an API server.
    /// </summary>
    /// <typeparam name="T">The JSON-serializable object type.</typeparam>
    /// <param name="link">The sender instance.</param>
    /// <param name="apiServerId">Target API server ID.</param>
    /// <param name="obj">The object to send.</param>
    public static void SendToApi<T>(this ILink link, string apiServerId, T obj) where T : class
    {
        link.SendToApi(apiServerId, JsonCPacketExtensions.Of(obj));
    }

    /// <summary>
    /// Sends a JSON request to an API server and awaits the reply.
    /// </summary>
    /// <typeparam name="T">The JSON-serializable object type.</typeparam>
    /// <param name="link">The sender instance.</param>
    /// <param name="apiServerId">Target API server ID.</param>
    /// <param name="obj">The request object.</param>
    /// <returns>The reply packet.</returns>
    public static Task<IPacket> RequestToApi<T>(this ILink link, string apiServerId, T obj) where T : class
    {
        return link.RequestToApi(apiServerId, JsonCPacketExtensions.Of(obj));
    }

    #endregion

    #region ILink - Stage Communication

    /// <summary>
    /// Sends a one-way JSON message to a stage on a Play server.
    /// </summary>
    /// <typeparam name="T">The JSON-serializable object type.</typeparam>
    /// <param name="link">The sender instance.</param>
    /// <param name="playServerId">Target Play server ID.</param>
    /// <param name="stageId">Target stage ID.</param>
    /// <param name="obj">The object to send.</param>
    public static void SendToStage<T>(this ILink link, string playServerId, long stageId, T obj) where T : class
    {
        link.SendToStage(playServerId, stageId, JsonCPacketExtensions.Of(obj));
    }

    /// <summary>
    /// Sends a JSON request to a stage and awaits the reply.
    /// </summary>
    /// <typeparam name="T">The JSON-serializable object type.</typeparam>
    /// <param name="link">The sender instance.</param>
    /// <param name="playServerId">Target Play server ID.</param>
    /// <param name="stageId">Target stage ID.</param>
    /// <param name="obj">The request object.</param>
    /// <returns>The reply packet.</returns>
    public static Task<IPacket> RequestToStage<T>(this ILink link, string playServerId, long stageId, T obj) where T : class
    {
        return link.RequestToStage(playServerId, stageId, JsonCPacketExtensions.Of(obj));
    }

    #endregion

    #region ILink - API Service Communication

    /// <summary>
    /// Sends a JSON message to an API server in the specified service using RoundRobin selection.
    /// </summary>
    /// <typeparam name="T">The JSON-serializable object type.</typeparam>
    /// <param name="link">The sender instance.</param>
    /// <param name="serviceId">Target service ID.</param>
    /// <param name="obj">The object to send.</param>
    public static void SendToApiService<T>(this ILink link, ushort serviceId, T obj) where T : class
    {
        link.SendToApiService(serviceId, JsonCPacketExtensions.Of(obj));
    }

    /// <summary>
    /// Sends a JSON message to an API server in the specified service using the specified selection policy.
    /// </summary>
    /// <typeparam name="T">The JSON-serializable object type.</typeparam>
    /// <param name="link">The sender instance.</param>
    /// <param name="serviceId">Target service ID.</param>
    /// <param name="obj">The object to send.</param>
    /// <param name="policy">Server selection policy.</param>
    public static void SendToApiService<T>(this ILink link, ushort serviceId, T obj, ServerSelectionPolicy policy) where T : class
    {
        link.SendToApiService(serviceId, JsonCPacketExtensions.Of(obj), policy);
    }

    /// <summary>
    /// Sends a JSON request to an API server in the specified service and awaits the reply (RoundRobin).
    /// </summary>
    /// <typeparam name="T">The JSON-serializable object type.</typeparam>
    /// <param name="link">The sender instance.</param>
    /// <param name="serviceId">Target service ID.</param>
    /// <param name="obj">The request object.</param>
    /// <returns>The reply packet.</returns>
    public static Task<IPacket> RequestToApiService<T>(this ILink link, ushort serviceId, T obj) where T : class
    {
        return link.RequestToApiService(serviceId, JsonCPacketExtensions.Of(obj));
    }

    /// <summary>
    /// Sends a JSON request to an API server in the specified service and awaits the reply with policy.
    /// </summary>
    /// <typeparam name="T">The JSON-serializable object type.</typeparam>
    /// <param name="link">The sender instance.</param>
    /// <param name="serviceId">Target service ID.</param>
    /// <param name="obj">The request object.</param>
    /// <param name="policy">Server selection policy.</param>
    /// <returns>The reply packet.</returns>
    public static Task<IPacket> RequestToApiService<T>(this ILink link, ushort serviceId, T obj, ServerSelectionPolicy policy) where T : class
    {
        return link.RequestToApiService(serviceId, JsonCPacketExtensions.Of(obj), policy);
    }

    #endregion

    #region ILink - System Communication

    /// <summary>
    /// Sends a one-way JSON system message to a server.
    /// </summary>
    /// <typeparam name="T">The JSON-serializable object type.</typeparam>
    /// <param name="link">The sender instance.</param>
    /// <param name="serverId">Target server ID.</param>
    /// <param name="obj">The object to send.</param>
    public static void SendToSystem<T>(this ILink link, string serverId, T obj) where T : class
    {
        link.SendToSystem(serverId, JsonCPacketExtensions.Of(obj));
    }

    /// <summary>
    /// Sends a JSON system request and awaits the reply.
    /// </summary>
    /// <typeparam name="T">The JSON-serializable object type.</typeparam>
    /// <param name="link">The sender instance.</param>
    /// <param name="serverId">Target server ID.</param>
    /// <param name="obj">The request object.</param>
    /// <returns>The reply packet.</returns>
    public static Task<IPacket> RequestToSystem<T>(this ILink link, string serverId, T obj) where T : class
    {
        return link.RequestToSystem(serverId, JsonCPacketExtensions.Of(obj));
    }

    #endregion

    #region IApiLink - Stage Creation

    /// <summary>
    /// Creates a new stage on a Play server with a JSON payload.
    /// </summary>
    /// <typeparam name="T">The JSON-serializable type for the creation payload.</typeparam>
    /// <param name="link">The API sender instance.</param>
    /// <param name="playNid">Target Play server NID.</param>
    /// <param name="stageType">Type of stage to create.</param>
    /// <param name="stageId">ID for the new stage.</param>
    /// <param name="payload">JSON creation payload.</param>
    /// <returns>Result containing the error code and create response.</returns>
    public static Task<CreateStageResult> CreateStage<T>(
        this IApiLink link,
        string playNid,
        string stageType,
        long stageId,
        T payload) where T : class
    {
        return link.CreateStage(playNid, stageType, stageId, JsonCPacketExtensions.Of(payload));
    }

    /// <summary>
    /// Gets an existing stage or creates a new one if it doesn't exist, with a JSON payload.
    /// </summary>
    /// <typeparam name="T">The JSON-serializable type for the creation payload.</typeparam>
    /// <param name="link">The API sender instance.</param>
    /// <param name="playNid">Target Play server NID.</param>
    /// <param name="stageType">Type of stage to create if needed.</param>
    /// <param name="stageId">ID for the stage.</param>
    /// <param name="createPayload">JSON creation payload (used if stage doesn't exist).</param>
    /// <returns>Result containing whether stage was created and the response.</returns>
    public static Task<GetOrCreateStageResult> GetOrCreateStage<T>(
        this IApiLink link,
        string playNid,
        string stageType,
        long stageId,
        T createPayload) where T : class
    {
        return link.GetOrCreateStage(playNid, stageType, stageId, JsonCPacketExtensions.Of(createPayload));
    }

    #endregion

    #region IStageLink - Client Communication

    /// <summary>
    /// Sends a JSON message to a specific client from a Stage.
    /// </summary>
    /// <typeparam name="T">The JSON-serializable object type.</typeparam>
    /// <param name="link">The stage sender instance.</param>
    /// <param name="sessionServerId">The session server ID.</param>
    /// <param name="sid">The session ID.</param>
    /// <param name="obj">The object to send.</param>
    public static void SendToClient<T>(this IStageLink link, string sessionServerId, long sid, T obj) where T : class
    {
        link.SendToClient(sessionServerId, sid, JsonCPacketExtensions.Of(obj));
    }

    #endregion

    #region IActorLink - Client Communication

    /// <summary>
    /// Sends a JSON message directly to the connected client from an Actor.
    /// </summary>
    /// <typeparam name="T">The JSON-serializable object type.</typeparam>
    /// <param name="link">The actor sender instance.</param>
    /// <param name="obj">The object to send.</param>
    public static void SendToClient<T>(this IActorLink link, T obj) where T : class
    {
        link.SendToClient(JsonCPacketExtensions.Of(obj));
    }

    #endregion
}
