﻿using PlayHouse.Production.Api;
using PlayHouse.Production.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PlayHouse.Service.Shared.Reflection;

internal class SystemHandlerRegister : ISystemHandlerRegister
{

    private readonly Dictionary<int, SystemHandler> _handles = new Dictionary<int, SystemHandler>();
    public Dictionary<int, SystemHandler> Handles => _handles;

    public void Add(int msgId, SystemHandler handler)
    {
        if (_handles.ContainsKey(msgId))
        {
            throw new InvalidOperationException($"Already exists message ID: {msgId}");
        }
        else
        {
            _handles[msgId] = handler;
        }
    }

    public SystemHandler GetHandler(int msgId)
    {
        if (_handles.TryGetValue(msgId, out var handler))
        {
            return handler;
        }
        else
        {
            throw new KeyNotFoundException($"Handler for message ID {msgId} not found");
        }
    }

}
