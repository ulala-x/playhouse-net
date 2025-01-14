using PlayHouse.Production.Shared;
using PlayHouse.Utils;

namespace PlayHouse.Communicator;

internal class ServerAddressResolver(
    string bindEndpoint,
    XServerInfoCenter serverInfoCenter,
    XClientCommunicator communicateClient,
    IService service,
    ISystemController system)
{
    private readonly LOG<ServerAddressResolver> _log = new();
    private CancellationTokenSource? _cts;

    private PeriodicTimer? _periodicTimer;

    public void Start()
    {
        _log.Info(() => $"Server address resolver start");

        _cts = new CancellationTokenSource();
        _periodicTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(ConstOption.AddressResolverPeriodMs));

        Task.Run(async () => await RunPeriodicTaskAsync(_cts.Token));
    }

    private async Task RunPeriodicTaskAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _periodicTimer!.WaitForNextTickAsync(cancellationToken))
            {
                await TimerCallbackAsync();
            }
        }
        catch (OperationCanceledException)
        {
            _log.Info(() => $"Server address resolver stopped.");
        }
        catch (Exception e)
        {
            _log.Error(() => $"Unexpected error in periodic task: {e}");
        }
        finally
        {
            _periodicTimer?.Dispose();
            _periodicTimer = null;
        }
    }

    private async Task TimerCallbackAsync()
    {
        try
        {
            var myServerInfo = new XServerInfo(
                bindEndpoint,
                service.ServiceId,
                service.ServerId,
                service.Nid,
                service.GetServiceType(),
                service.GetServerState(),
                service.GetActorCount(),
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            );

            var serverInfoList = await system.UpdateServerInfoAsync(myServerInfo);

            var updateList = serverInfoCenter.Update(serverInfoList.Select(XServerInfo.Of).ToList());

            foreach (var serverInfo in updateList)
            {
                switch (serverInfo.GetState())
                {
                    case ServerState.RUNNING:
                        communicateClient.Connect(serverInfo.GetNid(), serverInfo.GetBindEndpoint());
                        break;

                    case ServerState.DISABLE:
                        communicateClient.Disconnect(serverInfo.GetNid(), serverInfo.GetBindEndpoint());
                        break;
                }
            }
        }
        catch (Exception e)
        {
            _log.Error(() => $"Error in TimerCallbackAsync: {e}");
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _periodicTimer?.Dispose();
        _periodicTimer = null;
    }
}