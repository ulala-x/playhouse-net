#nullable enable

namespace PlayHouse.Abstractions.Api;

/// <summary>
/// Base class for stage operation results.
/// </summary>
public class StageResult
{
    /// <summary>
    /// Gets the error code from the operation (0 = success).
    /// </summary>
    public ushort ErrorCode { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="StageResult"/> class.
    /// </summary>
    /// <param name="errorCode">The error code.</param>
    public StageResult(ushort errorCode)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Gets whether the operation was successful.
    /// </summary>
    public bool IsSuccess => ErrorCode == 0;
}

/// <summary>
/// Result of a CreateStage operation.
/// </summary>
public class CreateStageResult : StageResult
{
    /// <summary>
    /// Gets the response packet from Stage.OnCreate().
    /// </summary>
    public IPacket CreateStageRes { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CreateStageResult"/> class.
    /// </summary>
    /// <param name="errorCode">The error code.</param>
    /// <param name="createStageRes">The create stage response packet.</param>
    public CreateStageResult(ushort errorCode, IPacket createStageRes)
        : base(errorCode)
    {
        CreateStageRes = createStageRes;
    }
}

/// <summary>
/// Result of a JoinStage operation.
/// </summary>
public class JoinStageResult : StageResult
{
    /// <summary>
    /// Gets the response packet from Actor join.
    /// </summary>
    public IPacket JoinStageRes { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="JoinStageResult"/> class.
    /// </summary>
    /// <param name="errorCode">The error code.</param>
    /// <param name="joinStageRes">The join stage response packet.</param>
    public JoinStageResult(ushort errorCode, IPacket joinStageRes)
        : base(errorCode)
    {
        JoinStageRes = joinStageRes;
    }
}

/// <summary>
/// Result of a GetOrCreateStage operation.
/// </summary>
public class GetOrCreateStageResult : StageResult
{
    /// <summary>
    /// Gets whether the stage was newly created.
    /// </summary>
    public bool IsCreated { get; }

    /// <summary>
    /// Gets the response packet from the operation.
    /// </summary>
    public IPacket Payload { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="GetOrCreateStageResult"/> class.
    /// </summary>
    /// <param name="errorCode">The error code.</param>
    /// <param name="isCreated">Whether the stage was newly created.</param>
    /// <param name="payload">The response payload.</param>
    public GetOrCreateStageResult(ushort errorCode, bool isCreated, IPacket payload)
        : base(errorCode)
    {
        IsCreated = isCreated;
        Payload = payload;
    }
}

/// <summary>
/// Result of a CreateJoinStage operation (create + join in one call).
/// </summary>
public class CreateJoinStageResult : StageResult
{
    /// <summary>
    /// Gets whether the stage was newly created.
    /// </summary>
    public bool IsCreated { get; }

    /// <summary>
    /// Gets the response packet from Stage.OnCreate().
    /// </summary>
    public IPacket CreateStageRes { get; }

    /// <summary>
    /// Gets the response packet from Actor join.
    /// </summary>
    public IPacket JoinStageRes { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CreateJoinStageResult"/> class.
    /// </summary>
    /// <param name="errorCode">The error code.</param>
    /// <param name="isCreated">Whether the stage was newly created.</param>
    /// <param name="createStageRes">The create stage response packet.</param>
    /// <param name="joinStageRes">The join stage response packet.</param>
    public CreateJoinStageResult(
        ushort errorCode,
        bool isCreated,
        IPacket createStageRes,
        IPacket joinStageRes)
        : base(errorCode)
    {
        IsCreated = isCreated;
        CreateStageRes = createStageRes;
        JoinStageRes = joinStageRes;
    }
}
