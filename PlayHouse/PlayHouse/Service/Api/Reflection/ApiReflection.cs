using PlayHouse.Production.Api.Aspectify;
using PlayHouse.Production.Shared;

namespace PlayHouse.Service.Api.Reflection;

internal class ApiReflection(IServiceProvider serviceProvider, ApiControllAspectifyManager aspectifyManager)
{
    private readonly ApiHandleReflectionInvoker _apiReflectionInvoker = new(serviceProvider,
        aspectifyManager.Get(),
        aspectifyManager.GetBackend());

    private readonly AsyncLocal<IServiceProvider> _localProvider = new();


    public async Task CallMethodAsync(IPacket packet, IApiSender apiSender)
    {
        if (_localProvider.Value == null)
        {
            await _apiReflectionInvoker.InvokeMethods(serviceProvider, packet.MsgId, packet, apiSender);
        }
        else
        {
            await _apiReflectionInvoker.InvokeMethods(_localProvider.Value, packet.MsgId, packet, apiSender);
        }
    }

    public async Task CallBackendMethodAsync(IPacket packet, IApiBackendSender apiBackendSender)
    {
        if (_localProvider.Value == null)
        {
            await _apiReflectionInvoker.InvokeBackendMethods(serviceProvider, packet.MsgId, packet, apiBackendSender);
        }
        else
        {
            await _apiReflectionInvoker.InvokeBackendMethods(serviceProvider, packet.MsgId, packet, apiBackendSender);
        }
    }

    public void Reset(IServiceProvider provider)
    {
        //serviceProvider = provider;
        _localProvider.Value = provider;
    }
}