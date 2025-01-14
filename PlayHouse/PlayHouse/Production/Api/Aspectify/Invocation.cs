using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace PlayHouse.Production.Api.Aspectify;

//public class Invocation(
//    object target,
//    MethodInfo method,
//    object[] arguments,
//    List<AspectifyAttribute> interceptors,
//    IServiceProvider serviceProvider)
//{
//    private int _currentInterceptorIndex = -1;

//    public dynamic? ReturnValue { get; private set; }

//    public MethodInfo Method { get; } = method;

//    public object[] Arguments { get; } = arguments;

//    public IServiceProvider ServiceProvider { get; } = serviceProvider;

//    public async Task Proceed()
//    {
//        _currentInterceptorIndex++;
//        if (_currentInterceptorIndex < interceptors.Count)
//        {
//            await interceptors[_currentInterceptorIndex].Intercept(this);
//        }
//        else
//        {
//            var returnType = Method.ReturnType;

//            if (returnType == typeof(Task))
//            {
//                // 반환 타입이 void
//                await (Task)Method.Invoke(target, Arguments)!;
//            }
//            else
//            {
//                // 반환 타입이 void가 아님
//                ReturnValue = await (dynamic)Method.Invoke(target, Arguments)!;
//            }
//        }
//    }
//}

public class Invocation(
    object target,
    MethodInfo method,
    object[] arguments,
    List<AspectifyAttribute> interceptors,
    IServiceProvider serviceProvider)
{
    private static readonly ConcurrentDictionary<string, Func<object, object[], object>> _compiledInvokers = new();
    private int _currentInterceptorIndex = -1;
    public dynamic? ReturnValue { get; private set; }
    public object[] Arguments => arguments;
    public MethodInfo Method { get; } = method;
    public IServiceProvider ServiceProvider { get; } = serviceProvider;

    public async Task Proceed()
    {
        _currentInterceptorIndex++;
        if (_currentInterceptorIndex < interceptors.Count)
        {
            await interceptors[_currentInterceptorIndex].Intercept(this);
        }
        else
        {
            var returnType = Method.ReturnType;

            if (returnType == typeof(Task))
            {
                // 반환 타입이 void (async)
                await (Task)InvokeMethod(target, Method, arguments)!;
            }
            else if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                // 반환 타입이 Task<T>
                var result = await (dynamic)InvokeMethod(target, Method, arguments)!;
                ReturnValue = result;
            }
            else
            {
                // 반환 타입이 일반 값
                ReturnValue = InvokeMethod(target, Method, arguments);
            }
        }
    }

    private static object? InvokeMethod(object target, MethodInfo method, object[] arguments)
    {
        // 캐싱된 Invoker 가져오기
        var invoker = GetOrCreateInvoker(method);
        return invoker(target, arguments);
    }

    private static Func<object, object[], object> GetOrCreateInvoker(MethodInfo method)
    {
        var key = $"{method.DeclaringType!.FullName}.{method.Name}";
        if (!_compiledInvokers.TryGetValue(key, out var invoker))
        {
            invoker = CompileMethodInvoker(method);
            _compiledInvokers[key] = invoker;
        }

        return invoker;
    }

    private static Func<object, object[], object> CompileMethodInvoker(MethodInfo method)
    {
        var instanceParameter = Expression.Parameter(typeof(object), "instance");
        var argumentsParameter = Expression.Parameter(typeof(object[]), "arguments");

        var call = Expression.Call(
            Expression.Convert(instanceParameter, method.DeclaringType!),
            method,
            method.GetParameters().Select((p, i) =>
                Expression.Convert(
                    Expression.ArrayIndex(argumentsParameter, Expression.Constant(i)),
                    p.ParameterType)));

        var lambda = Expression.Lambda<Func<object, object[], object>>(
            Expression.Convert(call, typeof(object)),
            instanceParameter, argumentsParameter);

        return lambda.Compile();
    }
}