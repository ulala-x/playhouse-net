#nullable enable

namespace PlayHouse.Abstractions.Api;

/// <summary>
/// Base class for stage operation results.
/// </summary>
public class StageResult
{
    /// <summary>
    /// Gets whether the operation was successful.
    /// </summary>
    /// <remarks>
    /// This corresponds to the bool result from IStage.OnCreate() and similar methods.
    /// </remarks>
    public bool Result { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="StageResult"/> class.
    /// </summary>
    /// <param name="result">Whether the operation was successful.</param>
    public StageResult(bool result)
    {
        Result = result;
    }
}

/// <summary>
/// Result of a CreateStage operation.
/// </summary>
/// <remarks>
/// Contains the result from IStage.OnCreate().
/// - Result=true: Stage creation succeeded
/// - Result=false: Stage creation failed
/// </remarks>
public class CreateStageResult : StageResult
{
    /// <summary>
    /// Gets the response packet from Stage.OnCreate().
    /// </summary>
    public IPacket CreateStageRes { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CreateStageResult"/> class.
    /// </summary>
    /// <param name="result">Whether the creation was successful.</param>
    /// <param name="createStageRes">The create stage response packet.</param>
    public CreateStageResult(bool result, IPacket createStageRes)
        : base(result)
    {
        CreateStageRes = createStageRes;
    }
}

/// <summary>
/// Result of a GetOrCreateStage operation.
/// </summary>
/// <remarks>
/// Result/IsCreated combinations:
/// - Result=true, IsCreated=false: Stage already existed (found existing)
/// - Result=true, IsCreated=true: New stage created successfully
/// - Result=false, IsCreated=false: New stage creation failed
/// </remarks>
public class GetOrCreateStageResult : StageResult
{
    /// <summary>
    /// Gets whether the stage was newly created.
    /// </summary>
    /// <remarks>
    /// - true: A new stage was created
    /// - false: Stage already existed OR creation failed (check Result)
    /// </remarks>
    public bool IsCreated { get; }

    /// <summary>
    /// Gets the response packet from the operation.
    /// </summary>
    public IPacket Payload { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="GetOrCreateStageResult"/> class.
    /// </summary>
    /// <param name="result">Whether the operation was successful.</param>
    /// <param name="isCreated">Whether the stage was newly created.</param>
    /// <param name="payload">The response payload.</param>
    public GetOrCreateStageResult(bool result, bool isCreated, IPacket payload)
        : base(result)
    {
        IsCreated = isCreated;
        Payload = payload;
    }
}

