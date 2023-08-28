﻿using PlayHouse.Communicator.PlaySocket;
using PlayHouse.Communicator;
using PlayHouse.Production;
using PlayHouse.Production.Api;
using CommonLib;
using Microsoft.Extensions.DependencyInjection;

namespace PlayHouse.Service.Api
{
    public class ApiServer : IServer
    {
        private readonly CommonOption _commonOption;
        private readonly ApiOption _apiOption;
        private Communicator.Communicator? _communicator;
        private Func<IServiceProvider> _serviceProviderFunc;
        private IServiceCollection  _serviceCollection;

        public ApiServer(
            CommonOption commonOption, 
            ApiOption apiOption, 
            IServiceCollection serviceCollection,
            Func<IServiceProvider> serviceProviderFunc
            )
        {
            _commonOption = commonOption;
            _apiOption = apiOption;
            _serviceCollection = serviceCollection;
            _serviceProviderFunc = serviceProviderFunc;
        }

        public void Start()
        {
            
            var communicatorOption = new CommunicatorOption.Builder()
              .SetPort(_commonOption.Port)
              .SetServerSystem(_commonOption.ServerSystem!)
              .SetShowQps(_commonOption.ShowQps)
              .Build();

            var bindEndpoint = communicatorOption.BindEndpoint;
            var serviceId = _commonOption.ServiceId;

            PooledBuffer.Init(_commonOption.MaxBufferPoolSize);

            var requestCache = new RequestCache(_commonOption.RequestTimeoutSec);
            var storageClient = new RedisStorageClient(_commonOption.RedisIp, _commonOption.RedisPort);
            storageClient.Connect();

            var serverInfoCenter = new XServerInfoCenter();

            var communicateServer = new XServerCommunicator(PlaySocketFactory.CreatePlaySocket(new SocketConfig(), bindEndpoint));
            var communicateClient = new XClientCommunicator(PlaySocketFactory.CreatePlaySocket(new SocketConfig(), bindEndpoint));

            var sender = new XSender(serviceId, communicateClient, requestCache);

            var nodeId = storageClient.GetNodeId(bindEndpoint);

            var systemPanel = new XSystemPanel(serverInfoCenter, communicateClient, nodeId);


            _serviceCollection.AddSingleton<ISystemPanel, XSystemPanel>();
            _serviceCollection.AddSingleton<ISender,XSender>();
            XServiceProvider.Instance = _serviceProviderFunc.Invoke();

            ControlContext.BaseSender = sender;
            ControlContext.SystemPanel = systemPanel;


            var service = new ApiProcessor(serviceId, _apiOption, requestCache, communicateClient, sender, systemPanel);
            _communicator = new Communicator.Communicator(
                communicatorOption,
                requestCache,
                serverInfoCenter,
                service,
                storageClient,
                sender,
                systemPanel,
                communicateServer,
                communicateClient
            );

            _communicator!.Start();
        }

        public void Stop()
        {
            _communicator!.Stop();
        }

        public void AwaitTermination()
        {
            _communicator!.AwaitTermination();
        }
    }
}
