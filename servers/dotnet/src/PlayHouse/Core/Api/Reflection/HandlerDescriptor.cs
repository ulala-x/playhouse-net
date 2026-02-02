#nullable enable

using System.Linq.Expressions;
using System.Reflection;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Api;

namespace PlayHouse.Core.Api.Reflection;

/// <summary>
/// Stores handler metadata for per-request controller instantiation.
/// </summary>
internal sealed class HandlerDescriptor(Type controllerType, MethodInfo method)
{
    /// <summary>
    /// The controller type that contains the handler method.
    /// </summary>
    public Type ControllerType { get; } = controllerType;

    /// <summary>
    /// The handler method info.
    /// </summary>
    public MethodInfo Method { get; } = method;

    /// <summary>
    /// Compiled handler delegate for fast invocation.
    /// </summary>
    public Func<object, IPacket, IApiLink, Task> CompiledHandler { get; } = CompileHandler(controllerType, method);

    /// <summary>
    /// Compiles a MethodInfo into a fast delegate using expression trees.
    /// </summary>
    private static Func<object, IPacket, IApiLink, Task> CompileHandler(Type controllerType, MethodInfo method)
    {
        // Validate method signature: Task MethodName(IPacket, IApiLink)
        var parameters = method.GetParameters();
        if (parameters.Length != 2 ||
            !typeof(IPacket).IsAssignableFrom(parameters[0].ParameterType) ||
            !typeof(IApiLink).IsAssignableFrom(parameters[1].ParameterType) ||
            !typeof(Task).IsAssignableFrom(method.ReturnType))
        {
            throw new InvalidOperationException(
                $"Handler method '{method.Name}' must have signature: Task MethodName(IPacket, IApiLink)");
        }

        // Build expression tree: (object instance, IPacket packet, IApiLink link) => ((TController)instance).Method(packet, link)
        var instanceParam = Expression.Parameter(typeof(object), "instance");
        var packetParam = Expression.Parameter(typeof(IPacket), "packet");
        var senderParam = Expression.Parameter(typeof(IApiLink), "sender");

        var call = Expression.Call(
            Expression.Convert(instanceParam, controllerType),
            method,
            packetParam,
            senderParam);

        return Expression.Lambda<Func<object, IPacket, IApiLink, Task>>(
            call, instanceParam, packetParam, senderParam).Compile();
    }
}
