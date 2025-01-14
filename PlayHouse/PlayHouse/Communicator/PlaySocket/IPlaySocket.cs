using PlayHouse.Communicator.Message;

namespace PlayHouse.Communicator.PlaySocket;

internal interface IPlaySocket
{
    string GetBindEndpoint();
    void Bind();
    void Send(string nid, RoutePacket routerPacket);
    void Connect(string endPoint);
    RoutePacket? Receive();
    void Disconnect(string endPoint);

    void Close();

    string EndPoint();
    string Nid();
}