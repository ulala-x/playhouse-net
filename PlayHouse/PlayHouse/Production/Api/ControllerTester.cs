using PlayHouse.Production.Shared;
using PlayHouse.Service.Api.Reflection;

namespace PlayHouse.Production.Api;

public class ControllerTester
{
    private ApiReflection? _apiReflection;
    private ApiReflectionCallback? _apiReflectionCallback;

    internal void Init(ApiReflection apiReflection, ApiReflectionCallback apiReflectionCallback)
    {
        _apiReflection = apiReflection;
        _apiReflectionCallback = apiReflectionCallback;
    }

    public void Reset(IServiceProvider serviceProvider)
    {
        _apiReflection?.Reset(serviceProvider);
        _apiReflectionCallback?.Reset(serviceProvider);
    }

    public async Task CallMethodAsync(IPacket packet, IApiSender apiSender)
    {
        await _apiReflection!.CallMethodAsync(packet, apiSender);
    }

    public async Task CallBackendMethodAsync(IPacket packet, IApiBackendSender apiBackendSender)
    {
        await _apiReflection!.CallBackendMethodAsync(packet, apiBackendSender);
    }

    public async Task OnDisconnect(IApiSender apiSender)
    {
        await _apiReflectionCallback!.OnDisconnectAsync(apiSender);
    }
}