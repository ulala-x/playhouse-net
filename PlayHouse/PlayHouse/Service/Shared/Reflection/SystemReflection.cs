using PlayHouse.Production.Api.Aspectify;
using PlayHouse.Production.Shared;

namespace PlayHouse.Service.Shared.Reflection;

internal class SystemReflection(IServiceProvider serviceProvider)
{
    private readonly SystemHandleReflectionInvoker _reflectionInvoker =
        new(serviceProvider, new List<AspectifyAttribute>());

    public async Task CallMethodAsync(IPacket packet, ISystemPanel panel, ISender sender)
    {
        var msgId = packet.MsgId;
        await _reflectionInvoker.InvokeMethods(serviceProvider, msgId, new object[] { packet, panel, sender });
    }
}