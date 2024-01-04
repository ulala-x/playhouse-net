﻿using PlayHouse.Production;
using PlayHouse.Production.Api.Aspectify;

namespace PlayHouseTests.Service.Api.plain;

[AttributeUsage(AttributeTargets.Class)]
public class TestAspectifyAttribute : AspectifyAttribute
{
    public override async Task Intercept(Invocation invocation)
    {
        IPacket packet = (IPacket)invocation.Arguments[0];
        ReflectionTestResult.ResultMap[$"TestApiActionAttributeBefore_{packet.MsgId}"] = "BeforeExecution";
        await invocation.Proceed();
        ReflectionTestResult.ResultMap[$"TestApiActionAttributeAfter_{packet.MsgId}"] =  "AfterExecution";
    }
}

[AttributeUsage(AttributeTargets.Class)]
public class TestBackendAspectifyAttribute : AspectifyAttribute
{
    public override async Task Intercept(Invocation invocation)
    {
        IPacket packet = (IPacket)invocation.Arguments[0];
        ReflectionTestResult.ResultMap[$"TestBackendApiActionAttributeBefore_{packet.MsgId}"] =  "BeforeExecution";
        await invocation.Proceed();
        ReflectionTestResult.ResultMap[$"TestBackendApiActionAttributeAfter_{packet.MsgId}"] = "AfterExecution";
    }
}


[AttributeUsage(AttributeTargets.Method)]
public class TestMethodAspectifyAttribute : AspectifyAttribute
{
    public override async Task Intercept(Invocation invocation)
    {
        IPacket packet = (IPacket)invocation.Arguments[0];
        ReflectionTestResult.ResultMap[$"TestApiMethodActionAttributeBefore_{packet.MsgId}"] = "BeforeExecution";
        await invocation.Proceed();
        ReflectionTestResult.ResultMap[$"TestApiMethodActionAttributeAfter_{packet.MsgId}"] = "AfterExecution";
    }
}

[AttributeUsage(AttributeTargets.Method)]
public class TestBackendMethodAspectifyAttribute : AspectifyAttribute
{
    public override async Task Intercept(Invocation invocation)
    {
        IPacket packet = (IPacket)invocation.Arguments[0];
        ReflectionTestResult.ResultMap[$"TestBackendApiMethodActionAttributeBefore_{packet.MsgId}"] = "BeforeExecution";
        await invocation.Proceed();
        ReflectionTestResult.ResultMap[$"TestBackendApiMethodActionAttributeAfter_{packet.MsgId}"] = "AfterExecution";
    }
}

[AttributeUsage(AttributeTargets.Class)]
public class TestGlobalAspectifyAttribute : AspectifyAttribute
{
    public override async Task Intercept(Invocation invocation)
    {
        IPacket packet = (IPacket)invocation.Arguments[0];
        ReflectionTestResult.ResultMap[$"TestApiGlobalActionAttributeBefore_{packet.MsgId}"] =  "BeforeExecution";
        await invocation.Proceed();
        ReflectionTestResult.ResultMap[$"TestApiGlobalActionAttributeAfter_{packet.MsgId}"] =  "AfterExecution";
    }

}