#nullable enable

using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Api;
using PlayHouse.Abstractions.Play;
using PlayHouse.Runtime.ServerMesh.Discovery;

namespace PlayHouse.Extensions.Json;

/// <summary>
/// Extension methods for ISender and its derived interfaces to support JSON messages.
/// </summary>
public static class JsonSenderExtensions
{
    #region ISender - Reply

    /// <summary>
    /// Sends a JSON object as a reply to the current request.
    /// </summary>
    /// <typeparam name="T">The JSON-serializable object type.</typeparam>
    /// <param name="sender">The sender instance.</param>
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
    public static void Reply<T>(this ISender sender, T obj) where T : class
    {
        sender.Reply(JsonCPacketExtensions.Of(obj));
    }

    #endregion

    #region ISender - API Server Communication

    /// <summary>
    /// Sends a one-way JSON message to an API server.
    /// </summary>
    /// <typeparam name="T">The JSON-serializable object type.</typeparam>
    /// <param name="sender">The sender instance.</param>
    /// <param name="apiServerId">Target API server ID.</param>
    /// <param name="obj">The object to send.</param>
    public static void SendToApi<T>(this ISender sender, string apiServerId, T obj) where T : class
    {
        sender.SendToApi(apiServerId, JsonCPacketExtensions.Of(obj));
    }

    /// <summary>
    /// Sends a JSON request to an API server and awaits the reply.
    /// </summary>
    /// <typeparam name="T">The JSON-serializable object type.</typeparam>
    /// <param name="sender">The sender instance.</param>
    /// <param name="apiServerId">Target API server ID.</param>
    /// <param name="obj">The request object.</param>
    /// <returns>The reply packet.</returns>
    public static Task<IPacket> RequestToApi<T>(this ISender sender, string apiServerId, T obj) where T : class
    {
        return sender.RequestToApi(apiServerId, JsonCPacketExtensions.Of(obj));
    }

    #endregion

    #region ISender - Stage Communication

    /// <summary>
    /// Sends a one-way JSON message to a stage on a Play server.
    /// </summary>
    /// <typeparam name="T">The JSON-serializable object type.</typeparam>
    /// <param name="sender">The sender instance.</param>
    /// <param name="playServerId">Target Play server ID.</param>
    /// <param name="stageId">Target stage ID.</param>
    /// <param name="obj">The object to send.</param>
    public static void SendToStage<T>(this ISender sender, string playServerId, long stageId, T obj) where T : class
    {
        sender.SendToStage(playServerId, stageId, JsonCPacketExtensions.Of(obj));
    }

    /// <summary>
    /// Sends a JSON request to a stage and awaits the reply.
    /// </summary>
    /// <typeparam name="T">The JSON-serializable object type.</typeparam>
    /// <param name="sender">The sender instance.</param>
    /// <param name="playServerId">Target Play server ID.</param>
    /// <param name="stageId">Target stage ID.</param>
    /// <param name="obj">The request object.</param>
    /// <returns>The reply packet.</returns>
    public static Task<IPacket> RequestToStage<T>(this ISender sender, string playServerId, long stageId, T obj) where T : class
    {
        return sender.RequestToStage(playServerId, stageId, JsonCPacketExtensions.Of(obj));
    }

    #endregion

    #region ISender - API Service Communication

    /// <summary>
    /// Sends a JSON message to an API server in the specified service using RoundRobin selection.
    /// </summary>
    /// <typeparam name="T">The JSON-serializable object type.</typeparam>
    /// <param name="sender">The sender instance.</param>
    /// <param name="serviceId">Target service ID.</param>
    /// <param name="obj">The object to send.</param>
    public static void SendToApiService<T>(this ISender sender, ushort serviceId, T obj) where T : class
    {
        sender.SendToApiService(serviceId, JsonCPacketExtensions.Of(obj));
    }

    /// <summary>
    /// Sends a JSON message to an API server in the specified service using the specified selection policy.
    /// </summary>
    /// <typeparam name="T">The JSON-serializable object type.</typeparam>
    /// <param name="sender">The sender instance.</param>
    /// <param name="serviceId">Target service ID.</param>
    /// <param name="obj">The object to send.</param>
    /// <param name="policy">Server selection policy.</param>
    public static void SendToApiService<T>(this ISender sender, ushort serviceId, T obj, ServerSelectionPolicy policy) where T : class
    {
        sender.SendToApiService(serviceId, JsonCPacketExtensions.Of(obj), policy);
    }

    /// <summary>
    /// Sends a JSON request to an API server in the specified service and awaits the reply (RoundRobin).
    /// </summary>
    /// <typeparam name="T">The JSON-serializable object type.</typeparam>
    /// <param name="sender">The sender instance.</param>
    /// <param name="serviceId">Target service ID.</param>
    /// <param name="obj">The request object.</param>
    /// <returns>The reply packet.</returns>
    public static Task<IPacket> RequestToApiService<T>(this ISender sender, ushort serviceId, T obj) where T : class
    {
        return sender.RequestToApiService(serviceId, JsonCPacketExtensions.Of(obj));
    }

    /// <summary>
    /// Sends a JSON request to an API server in the specified service and awaits the reply with policy.
    /// </summary>
    /// <typeparam name="T">The JSON-serializable object type.</typeparam>
    /// <param name="sender">The sender instance.</param>
    /// <param name="serviceId">Target service ID.</param>
    /// <param name="obj">The request object.</param>
    /// <param name="policy">Server selection policy.</param>
    /// <returns>The reply packet.</returns>
    public static Task<IPacket> RequestToApiService<T>(this ISender sender, ushort serviceId, T obj, ServerSelectionPolicy policy) where T : class
    {
        return sender.RequestToApiService(serviceId, JsonCPacketExtensions.Of(obj), policy);
    }

    #endregion

    #region ISender - System Communication

    /// <summary>
    /// Sends a one-way JSON system message to a server.
    /// </summary>
    /// <typeparam name="T">The JSON-serializable object type.</typeparam>
    /// <param name="sender">The sender instance.</param>
    /// <param name="serverId">Target server ID.</param>
    /// <param name="obj">The object to send.</param>
    public static void SendToSystem<T>(this ISender sender, string serverId, T obj) where T : class
    {
        sender.SendToSystem(serverId, JsonCPacketExtensions.Of(obj));
    }

    /// <summary>
    /// Sends a JSON system request and awaits the reply.
    /// </summary>
    /// <typeparam name="T">The JSON-serializable object type.</typeparam>
    /// <param name="sender">The sender instance.</param>
    /// <param name="serverId">Target server ID.</param>
    /// <param name="obj">The request object.</param>
    /// <returns>The reply packet.</returns>
    public static Task<IPacket> RequestToSystem<T>(this ISender sender, string serverId, T obj) where T : class
    {
        return sender.RequestToSystem(serverId, JsonCPacketExtensions.Of(obj));
    }

    #endregion

    #region IApiSender - Stage Creation

    /// <summary>
    /// Creates a new stage on a Play server with a JSON payload.
    /// </summary>
    /// <typeparam name="T">The JSON-serializable type for the creation payload.</typeparam>
    /// <param name="sender">The API sender instance.</param>
    /// <param name="playNid">Target Play server NID.</param>
    /// <param name="stageType">Type of stage to create.</param>
    /// <param name="stageId">ID for the new stage.</param>
    /// <param name="payload">JSON creation payload.</param>
    /// <returns>Result containing the error code and create response.</returns>
    public static Task<CreateStageResult> CreateStage<T>(
        this IApiSender sender,
        string playNid,
        string stageType,
        long stageId,
        T payload) where T : class
    {
        return sender.CreateStage(playNid, stageType, stageId, JsonCPacketExtensions.Of(payload));
    }

    /// <summary>
    /// Gets an existing stage or creates a new one if it doesn't exist, with a JSON payload.
    /// </summary>
    /// <typeparam name="T">The JSON-serializable type for the creation payload.</typeparam>
    /// <param name="sender">The API sender instance.</param>
    /// <param name="playNid">Target Play server NID.</param>
    /// <param name="stageType">Type of stage to create if needed.</param>
    /// <param name="stageId">ID for the stage.</param>
    /// <param name="createPayload">JSON creation payload (used if stage doesn't exist).</param>
    /// <returns>Result containing whether stage was created and the response.</returns>
    public static Task<GetOrCreateStageResult> GetOrCreateStage<T>(
        this IApiSender sender,
        string playNid,
        string stageType,
        long stageId,
        T createPayload) where T : class
    {
        return sender.GetOrCreateStage(playNid, stageType, stageId, JsonCPacketExtensions.Of(createPayload));
    }

    #endregion

    #region IStageSender - Client Communication

    /// <summary>
    /// Sends a JSON message to a specific client from a Stage.
    /// </summary>
    /// <typeparam name="T">The JSON-serializable object type.</typeparam>
    /// <param name="sender">The stage sender instance.</param>
    /// <param name="sessionServerId">The session server ID.</param>
    /// <param name="sid">The session ID.</param>
    /// <param name="obj">The object to send.</param>
    public static void SendToClient<T>(this IStageSender sender, string sessionServerId, long sid, T obj) where T : class
    {
        sender.SendToClient(sessionServerId, sid, JsonCPacketExtensions.Of(obj));
    }

    #endregion

    #region IActorSender - Client Communication

    /// <summary>
    /// Sends a JSON message directly to the connected client from an Actor.
    /// </summary>
    /// <typeparam name="T">The JSON-serializable object type.</typeparam>
    /// <param name="sender">The actor sender instance.</param>
    /// <param name="obj">The object to send.</param>
    public static void SendToClient<T>(this IActorSender sender, T obj) where T : class
    {
        sender.SendToClient(JsonCPacketExtensions.Of(obj));
    }

    #endregion
}
