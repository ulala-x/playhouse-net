﻿using PlayHouse.Communicator.Message;

namespace PlayHouse.Communicator;
public interface IClientCommunicator
{
    void Connect(string endpoint);
    void Send(string endpoint, RoutePacket routePacket);
    void Communicate();
    void Disconnect(string endpoint);
    void Stop();
}

