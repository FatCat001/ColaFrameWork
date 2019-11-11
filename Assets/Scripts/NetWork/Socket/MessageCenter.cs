﻿/// <summary>
/// 网络消息处理中心
/// 缓存消息，然后分帧泵到lua端进行处理
/// </summary>

using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;


public struct NetMessageData
{
    public int protocol;
    public byte[] _eventData;
}

public class NetMessageCenter : IManager
{
    [LuaInterface.NoToLua]
    public Queue<NetMessageData> NetMessageQueue;

    [LuaInterface.LuaByteBuffer]
    public Action<byte[]> OnMessage;

    private static NetMessageCenter _instance = null;

    public static NetMessageCenter Instance
    {
        get
        {
            if (null == _instance)
            {
                _instance = new NetMessageCenter();
            }
            return _instance;
        }
    }

    /// <summary>
    /// 每帧默认处理2个协议
    /// </summary>
    private int perHandleCnt = 3;

    public float TimeSinceUpdate { get; set; }

    private NetMessageCenter()
    {

    }

    [LuaInterface.NoToLua]
    public void Init()
    {
        NetMessageQueue = new Queue<NetMessageData>();
    }

    public void SetPerFrameHandleCnt(int value)
    {
        perHandleCnt = value;
    }

    [LuaInterface.NoToLua]
    public void Update(float deltaTime)
    {
        int handledCnt = 0;
        while (NetMessageQueue.Count > 0)
        {
            lock (NetMessageQueue)
            {
                NetMessageData tmpNetMessageData = NetMessageQueue.Dequeue();
                handledCnt++;
                try
                {
                    if (null != OnMessage)
                    {
                        OnMessage(tmpNetMessageData._eventData);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError("try to handle message error!");
                }
                if (handledCnt >= perHandleCnt)
                {
                    break;
                }
            }
        }
    }

    [LuaInterface.NoToLua]
    public void Dispose()
    {
        if (null != NetMessageQueue)
        {
            NetMessageQueue.Clear();
        }
        NetMessageQueue = null;
        OnMessage = null;
    }
}