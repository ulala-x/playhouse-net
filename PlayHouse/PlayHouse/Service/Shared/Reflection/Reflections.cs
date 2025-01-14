using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using PlayHouse.Production.Api.Aspectify;
using PlayHouse.Production.Shared;

namespace PlayHouse.Service.Shared.Reflection;

public class ReflectionMethod
{
    public ReflectionMethod(string msgId, string className, MethodInfo method,
        IEnumerable<AspectifyAttribute> targetFilters, IEnumerable<AspectifyAttribute> classFilters)
    {
        MsgId = msgId;
        ClassName = className;
        Method = method;
        Filters.AddRange(targetFilters);
        Filters.AddRange(classFilters);
        Filters.AddRange(method.GetCustomAttributes(typeof(AspectifyAttribute), true)
            .Select(e => (AspectifyAttribute)e));
    }

    public ReflectionMethod(string msgId, string className, MethodInfo method)
    {
        MsgId = msgId;
        ClassName = className;
        Method = method;
        Filters.AddRange(method.GetCustomAttributes(typeof(AspectifyAttribute), true)
            .Select(e => (AspectifyAttribute)e));
    }

    public string MsgId { get; set; }
    public string ClassName { get; set; }
    public MethodInfo Method { get; set; }
    public List<AspectifyAttribute> Filters { get; set; } = new();
}

public class ReflectionInstance(Type type, IEnumerable<AspectifyAttribute> filters, IServiceProvider serviceProvider)
{
    public object Instance { get; set; } = ActivatorUtilities.CreateInstance(serviceProvider, type);
    public Type Type { get; set; } = type;

    public IEnumerable<AspectifyAttribute> Filters { get; set; } = filters;

    public string Name => Type.FullName!;

    internal async Task Invoke(IServiceProvider serviceProvider, ReflectionMethod targetMethod,
        params object[] arguments)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var targetInstance = scope.ServiceProvider.GetRequiredService(Type);
        var invocation = new Invocation(targetInstance, targetMethod.Method, arguments, targetMethod.Filters,
            scope.ServiceProvider);
        await invocation.Proceed();
    }

    internal async Task<object> InvokeWithReturn(IServiceProvider serviceProvider, ReflectionMethod targetMethod,
        object[] arguments)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var targetInstance = scope.ServiceProvider.GetRequiredService(Type);
        var invocation = new Invocation(targetInstance, targetMethod.Method, arguments, targetMethod.Filters,
            scope.ServiceProvider);
        await invocation.Proceed();
        return invocation.ReturnValue!;
    }
}

internal class ReflectionOperator
{
    private readonly Type[] _findTypes;
    private readonly IServiceProvider _serviceProvider;

    public ReflectionOperator(IServiceProvider serviceProvider, params Type[] types)
    {
        _serviceProvider = serviceProvider;
        _findTypes = GetAllSubtypes(types);
    }

    public List<MethodInfo> GetMethodsBySignature(string methodName, Type returnType, params Type[] parameterTypes)
    {
        return _findTypes
            .SelectMany(type => type.GetMethods())
            .Where(method =>
                method.Name == methodName &&
                method.ReturnType == returnType &&
                method.GetParameters().Select(p => p.ParameterType).SequenceEqual(parameterTypes))
            .ToList();
    }

    private Type[] GetAllSubtypes(params Type[] subTypes)
    {
        var typeList = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false } &&
                           subTypes.Any(subType => subType.IsAssignableFrom(type)))
            .ToList();

        List<Type> result = [];
        foreach (var type in typeList)
        {
            var exist = _serviceProvider.GetService(type);
            if (exist != null)
            {
                result.Add(type);
            }
        }

        return result.ToArray();
    }


    public List<ReflectionInstance> GetInstanceBy(params Type[] targetTypes)
    {
        return _findTypes
            .Where(type =>
                type.IsClass && !type.IsAbstract && targetTypes.Any(targetType => targetType.IsAssignableFrom(type)))
            .Select(type =>
            {
                var apiFilters = type.GetCustomAttributes(typeof(AspectifyAttribute), true)
                    .Select(e => (AspectifyAttribute)e);
                return new ReflectionInstance(type, apiFilters, _serviceProvider);
            }).ToList();
    }


    internal List<MethodInfo> GetMethodsBy(Type targetType)
    {
        var methods = targetType.GetMethods().Select(e => e.Name).ToList();

        return _findTypes
            .Where(type => type.IsClass && !type.IsAbstract && targetType.IsAssignableFrom(type))
            .SelectMany(type => type.GetMethods())
            .Where(typeMethod => methods.Contains(typeMethod.Name) && typeMethod.IsPublic).ToList();
    }
}

internal class SystemHandleReflectionInvoker
{
    private readonly Dictionary<string, ReflectionInstance> _instances = new();
    private readonly Dictionary<string, string> _messageIndexChecker = new();
    private readonly Dictionary<string, ReflectionMethod> _methods = new();
    private readonly IEnumerable<AspectifyAttribute> _targetFilters;

    public SystemHandleReflectionInvoker(IServiceProvider serviceProvider,
        IEnumerable<AspectifyAttribute> targetFilters)
    {
        var type = typeof(ISystemController);
        _targetFilters = targetFilters;
        var reflections = new ReflectionOperator(serviceProvider, type);
        _instances = reflections.GetInstanceBy(type).ToDictionary(e => e.Name);
        ExtractMethodInfo(reflections);
    }

    public void ExtractMethodInfo(ReflectionOperator reflections)
    {
        reflections.GetMethodsBySignature("Handles", typeof(void), typeof(ISystemHandlerRegister)).ForEach(methodInfo =>
        {
            var className = methodInfo.DeclaringType!.FullName!;
            var systemInstance = _instances[className!]!;
            var handlerRegister = new SystemHandlerRegister();

            methodInfo.Invoke(systemInstance.Instance, new object[] { handlerRegister });

            foreach (var (key, value) in handlerRegister.Handles)
            {
                if (_messageIndexChecker.ContainsKey(key))
                {
                    throw new Exception(
                        $"registered methodName is duplicated - methodName:{key}, methods: {_messageIndexChecker[key]}, {value.GetMethodInfo().Name}");
                }

                _methods[key] =
                    new ReflectionMethod(key, className, value.Method, _targetFilters, systemInstance.Filters);
                _messageIndexChecker[key] = value.GetMethodInfo().Name;
            }
        });
    }

    public async Task InvokeMethods(IServiceProvider serviceProvider, string msgId, object[] arguments)
    {
        var method = _methods[msgId];

        if (method == null) throw new ServiceException.NotRegisterMethod($"not registered message methodName:{msgId}");
        if (!_instances.TryGetValue(method.ClassName, out var instance))
            throw new ServiceException.NotRegisterInstance(
                $"{method.ClassName}: reflection instance is not registered");

        await instance.Invoke(serviceProvider, method, arguments);
    }
}

internal class CallbackReflectionInvoker
{
    private readonly Dictionary<string, ReflectionInstance> _instances = new();
    private readonly Dictionary<string, ReflectionMethod> _methods = new();

    public CallbackReflectionInvoker(IServiceProvider serviceProvider, Type[] types)
    {
        var reflections = new ReflectionOperator(serviceProvider, types);

        foreach (var type in types)
        {
            foreach (var instance in reflections.GetInstanceBy(type))
            {
                _instances.Add(instance.Name, instance);
            }

            ;
        }

        ExtractMethodInfo(reflections, types);
    }

    private void ExtractMethodInfo(ReflectionOperator reflections, Type[] types)
    {
        foreach (var type in types)
        {
            reflections.GetMethodsBy(type).ForEach(methodInfo =>
            {
                var className = methodInfo.DeclaringType!.FullName!;
                //var systemInstance = _instances[className!]!;

                if (_methods.ContainsKey(methodInfo.Name) == false)
                {
                    _methods[methodInfo.Name] = new ReflectionMethod(string.Empty, className, methodInfo);
                }
                else
                {
                    throw new Exception("Only one class implementing IDisconnectCallback can exist");
                }
            });
        }
    }

    public async Task InvokeMethods(IServiceProvider serviceProvider, string methodName, object[] arguements)
    {
        var method = _methods[methodName];
        var instance = _instances[method.ClassName];
        await instance.Invoke(serviceProvider, method, arguements);
    }

    public async Task<object?> InvokeMethodsWithReturn(IServiceProvider serviceProvider, string methodName,
        object[] arguments)
    {
        var method = _methods[methodName];
        var instance = _instances[method.ClassName];
        return await instance.InvokeWithReturn(serviceProvider, method, arguments);
    }
}