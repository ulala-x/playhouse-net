﻿using FluentAssertions;
using Microsoft.Win32;
using Org.Ulalax.Playhouse.Protocol;
using PlayHouse;
using PlayHouse.Production.Api;
using PlayHouse.Production.Shared;

namespace PlayHouseTests.Service.Api.plain;


[TestAspectify]
[TestBackendAspectify]
internal class TestApiController : IApiController, IDisconnectCallback
{
    public void Handles(IHandlerRegister handlerRegister, IBackendHandlerRegister backendHandlerRegister)
    {
        handlerRegister.Add(ApiTestMsg1.Descriptor.Index, Test1);
        handlerRegister.Add(ApiTestMsg2.Descriptor.Index, Test2);
        handlerRegister.Add(ApiDefaultContentsExceptionTest.Descriptor.Index, TestApiDefaultContentsException);
        handlerRegister.Add(ApiContentsExceptionTest.Descriptor.Index, TestApiContentsException);

        backendHandlerRegister.Add(ApiTestMsg1.Descriptor.Index, Test3);
        backendHandlerRegister.Add(ApiTestMsg2.Descriptor.Index, Test4);
    }

    private Task TestApiContentsException(IPacket packet, IApiSender apiSender)
    {
        throw new Exception("test content TestApiContentsException");
    }

    private Task TestApiDefaultContentsException(IPacket packet, IApiSender apiSender)
    {
        throw new Exception("test content TestApiDefaultContentsException");
    }


    [TestMethodAspectify]
    public async Task Test1(IPacket packet, IApiSender apiSender)
    {
        var message = ApiTestMsg1.Parser.ParseFrom(packet.Payload.DataSpan);
        ReflectionTestResult.ResultMap[$"{this.GetType().Name}_Test1"] = message.TestMsg;
        await Task.CompletedTask;
    }

    public async Task Test2(IPacket packet, IApiSender apiSender)
    {
        var message = ApiTestMsg2.Parser.ParseFrom(packet.Payload.DataSpan);
        ReflectionTestResult.ResultMap[$"{this.GetType().Name}_Test2"] = message.TestMsg;
        await Task.CompletedTask;
    }

    [TestBackendMethodAspectify]
    public async Task Test3(IPacket packet, IApiBackendSender apiSender)
    {
        var message = ApiTestMsg1.Parser.ParseFrom(packet.Payload.DataSpan);
        ReflectionTestResult.ResultMap[$"{this.GetType().Name}_Test3"] = message.TestMsg;
        await Task.CompletedTask;
    }

    public async Task Test4(IPacket packet, IApiBackendSender apiSender)
    {
        var message = ApiTestMsg2.Parser.ParseFrom(packet.Payload.DataSpan);
        ReflectionTestResult.ResultMap[$"{this.GetType().Name}_Test4"] = message.TestMsg;
        await Task.CompletedTask;
    }

    public async Task OnDisconnectAsync(IApiSender apiSender)
    {
        ReflectionTestResult.ResultMap[$"OnDisconnectAsync"] = "OnDisconnectAsync";
        await Task.CompletedTask;
    }
}


internal class TestSystemController : ISystemController, IUpdateServerInfoCallback
{
    public void Handles(ISystemHandlerRegister handlerRegister)
    {
        handlerRegister.Add(SystemHandlerTestMsg.Descriptor.Index, Test);
    }

    public async Task Test(IPacket packet, ISystemPanel panel, ISender sender)
    {
        var message = SystemHandlerTestMsg.Parser.ParseFrom(packet.Payload.DataSpan);
        ReflectionTestResult.ResultMap[$"{this.GetType().Name}_Test"] = message.TestMsg;
        await Task.CompletedTask;
    }

    public async Task<List<IServerInfo>> UpdateServerInfoAsync(IServerInfo serverInfo)
    {
        ReflectionTestResult.ResultMap[$"{this.GetType().Name}_UpdateServerInfoAsync"] = "UpdateServerInfoAsync";
        await Task.CompletedTask;
        return new List<IServerInfo>() {  serverInfo };
    }
}
