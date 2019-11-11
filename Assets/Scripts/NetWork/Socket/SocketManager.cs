﻿//----------------------------------------------
//            ColaFramework
// Copyright © 2018-2049 ColaFramework 马三小伙儿
//----------------------------------------------

using UnityEngine;
using System.Collections;
using System.Net.Sockets;
using System.Threading;
using System.Net;
using System;


namespace ColaFramework
{
    /// <summary>
    /// Socket网络套接字管理器
    /// </summary>
    public class SocketManager : IDisposable
    {
        public static SocketManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new SocketManager();
                }
                return _instance;
            }
        }
        public bool IsConnceted { get { return _isConnected; } }

        private static SocketManager _instance;
        private string _currIP;
        private int _currPort;
        private int _timeOutMilliSec = 5000;
        private float pingloopSec = 1.0f;
        private long pingTimerId = -1;
        private byte[] pingBytes = System.Text.Encoding.UTF8.GetBytes(AppConst.AppName);

        private bool _isConnected = false;

        private Socket clientSocket = null;
        private Thread receiveThread = null;

        /// <summary>
        /// 网络数据缓存器
        /// </summary>
        private DataBuffer _databuffer = new DataBuffer();

        /// <summary>
        /// 数据接收缓冲区
        /// </summary>
        byte[] _tmpReceiveBuff = new byte[4096];

        /// <summary>
        /// 网络数据结构
        /// </summary>
        private sSocketData _socketData = new sSocketData();

        #region 对外回调
        public Action OnTimeOut;
        public Action OnFailed;
        public Action OnConnected;
        public Action OnReConnected;
        public Action OnClose;
        #endregion

        #region 对外基本方法
        /// <summary>
        /// 向服务器发送消息
        /// </summary>
        /// <param name="_protocalType"></param>
        /// <param name="byteBuffer"></param>
        [LuaInterface.LuaByteBuffer]
        public void SendMsg(int protocol, byte[] byteMsg)
        {
            //SendMsgBase(eProtocalCommand.sc_message, byteBuffer.ToBytes());

            //Test Code
            NetMessageData tmpNetMessageData = new NetMessageData();
            tmpNetMessageData.protocol = protocol;
            tmpNetMessageData._eventData = byteMsg;

            //锁死消息中心消息队列，并添加数据
            lock (NetMessageCenter.Instance.NetMessageQueue)
            {
                NetMessageCenter.Instance.NetMessageQueue.Enqueue(tmpNetMessageData);
            }
        }

        /// <summary>
        /// 连接服务器
        /// </summary>
        /// <param name="_currIP"></param>
        /// <param name="_currPort"></param>
        public void Connect(string _currIP, int _currPort)
        {
            if (!IsConnceted)
            {
                this._currIP = _currIP;
                this._currPort = _currPort;
                _onConnet();
            }
        }

        public void Close()
        {
            _close();
        }

        /// <summary>
        /// 设置超时的阈值
        /// </summary>
        /// <param name="milliSec"></param>
        public void SetTimeOut(int milliSec)
        {
            _timeOutMilliSec = milliSec;
        }
        #endregion

        /// <summary>
        /// 断开
        /// </summary>
        private void _close()
        {
            if (!_isConnected)
                return;

            _isConnected = false;
            //停止pingServer
            Timer.Cancel(pingTimerId);

            if (receiveThread != null)
            {
                receiveThread.Abort();
                receiveThread = null;
            }

            if (clientSocket != null && clientSocket.Connected)
            {
                clientSocket.Close();
                clientSocket = null;
            }
            if (null != OnClose)
            {
                OnClose();
            }
        }

        /// <summary>
        /// 重连机制
        /// </summary>
        private void _ReConnect()
        {
            //停止pingServer
            Timer.Cancel(pingTimerId);
            if (null != OnReConnected)
            {
                OnReConnected();
            }
        }

        /// <summary>
        /// 连接
        /// </summary>
        private void _onConnet()
        {
            try
            {
                clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);//创建套接字
                IPAddress ipAddress = IPAddress.Parse(_currIP);//解析IP地址
                IPEndPoint ipEndpoint = new IPEndPoint(ipAddress, _currPort);
                IAsyncResult result = clientSocket.BeginConnect(ipEndpoint, new AsyncCallback(_onConnect_Sucess), clientSocket);//异步连接
                bool success = result.AsyncWaitHandle.WaitOne(_timeOutMilliSec, true);
                if (!success) //超时
                {
                    _onConnect_Outtime();
                }
            }
            catch (System.Exception _e)
            {
                _onConnect_Fail();
            }
        }

        private void _onConnect_Sucess(IAsyncResult iar)
        {
            try
            {
                Socket client = (Socket)iar.AsyncState;
                client.EndConnect(iar);

                receiveThread = new Thread(new ThreadStart(_onReceiveSocket));
                receiveThread.IsBackground = true;
                receiveThread.Start();
                _isConnected = true;
                Debug.Log("连接成功");

                //启动pingServer
                pingTimerId = Timer.RunBySeconds(pingloopSec, PingServer, null);

                if (null != OnConnected)
                {
                    OnConnected();
                }
            }
            catch (Exception _e)
            {
                Close();
            }
        }

        /// <summary>
        /// 连接服务器超时
        /// </summary>
        private void _onConnect_Outtime()
        {
            if (null != OnTimeOut)
            {
                OnTimeOut();
            }
            _close();
        }

        /// <summary>
        /// 连接服务器失败
        /// </summary>
        private void _onConnect_Fail()
        {
            if (null != OnFailed)
            {
                OnFailed();
            }
            _close();
        }

        /// <summary>
        /// 发送消息结果回调，可判断当前网络状态
        /// </summary>
        /// <param name="asyncSend"></param>
        private void _onSendMsg(IAsyncResult asyncSend)
        {
            try
            {
                Socket client = (Socket)asyncSend.AsyncState;
                client.EndSend(asyncSend);
            }
            catch (Exception e)
            {
                Debug.Log("send msg exception:" + e.StackTrace);
            }
        }

        /// <summary>
        /// 接收网络数据
        /// </summary>
        private void _onReceiveSocket()
        {
            while (true)
            {
                if (!clientSocket.Connected)
                {
                    _isConnected = false;
                    _ReConnect();
                    break;
                }
                try
                {
                    int receiveLength = clientSocket.Receive(_tmpReceiveBuff);
                    if (receiveLength > 0)
                    {
                        _databuffer.AddBuffer(_tmpReceiveBuff, receiveLength);//将收到的数据添加到缓存器中
                        while (_databuffer.GetData(out _socketData))//取出一条完整数据
                        {
                            //只有消息协议才进入队列
                            if(eProtocalCommand.sc_message == _socketData._protocallType)
                            {
                                NetMessageData tmpNetMessageData = new NetMessageData();
                                tmpNetMessageData._eventType = _socketData._protocallType;
                                tmpNetMessageData._eventData = _socketData.data;

                                //锁死消息中心消息队列，并添加数据
                                lock (NetMessageCenter.Instance.NetMessageQueue)
                                {
                                    NetMessageCenter.Instance.NetMessageQueue.Enqueue(tmpNetMessageData);
                                }
                            }
                            else
                            {
                                //TODO:处理ping协议
                            }

                        }
                    }
                }
                catch (System.Exception e)
                {
                    clientSocket.Disconnect(true);
                    clientSocket.Shutdown(SocketShutdown.Both);
                    clientSocket.Close();
                    break;
                }
            }
        }


        /// <summary>
        /// 数据转网络结构
        /// </summary>
        /// <param name="_protocalType"></param>
        /// <param name="_data"></param>
        /// <returns></returns>
        private sSocketData BytesToSocketData(eProtocalCommand _protocalType, byte[] _data)
        {
            sSocketData tmpSocketData = new sSocketData();
            tmpSocketData.buffLength = Constants.HEAD_LEN + _data.Length;
            tmpSocketData._protocallType = _protocalType;
            tmpSocketData.dataLength = _data.Length;
            tmpSocketData.data = _data;
            return tmpSocketData;
        }

        /// <summary>
        /// 网络结构转数据
        /// </summary>
        /// <param name="tmpSocketData"></param>
        /// <returns></returns>
        private byte[] SocketDataToBytes(sSocketData tmpSocketData)
        {
            byte[] _tmpBuff = new byte[tmpSocketData.buffLength];
            byte[] _tmpBuffLength = BitConverter.GetBytes(tmpSocketData.buffLength);
            byte[] _tmpDataLenght = BitConverter.GetBytes((UInt16)tmpSocketData._protocallType);

            Array.Copy(_tmpBuffLength, 0, _tmpBuff, 0, Constants.HEAD_DATA_LEN);//缓存总长度
            Array.Copy(_tmpDataLenght, 0, _tmpBuff, Constants.HEAD_DATA_LEN, Constants.HEAD_TYPE_LEN);//协议类型
            Array.Copy(tmpSocketData.data, 0, _tmpBuff, Constants.HEAD_LEN, tmpSocketData.dataLength);//协议数据

            return _tmpBuff;
        }

        /// <summary>
        /// 合并协议，数据
        /// </summary>
        /// <param name="_protocalType"></param>
        /// <param name="_data"></param>
        /// <returns></returns>
        private byte[] DataToBytes(eProtocalCommand _protocalType, byte[] _data)
        {
            return SocketDataToBytes(BytesToSocketData(_protocalType, _data));
        }

        /// <summary>
        /// 发送消息基本方法
        /// </summary>
        /// <param name="_protocalType"></param>
        /// <param name="_data"></param>
        private void SendMsgBase(eProtocalCommand _protocalType, byte[] _data)
        {
            if (clientSocket == null || !clientSocket.Connected)
            {
                _ReConnect();
                return;
            }

            byte[] _msgdata = DataToBytes(_protocalType, _data);
            clientSocket.BeginSend(_msgdata, 0, _msgdata.Length, SocketFlags.None, new AsyncCallback(_onSendMsg), clientSocket);
        }

        /// <summary>
        /// PingServer
        /// </summary>
        private void PingServer()
        {
            SendMsgBase(eProtocalCommand.sc_ping, pingBytes);
        }

        public void Dispose()
        {
            _close();
            OnClose = null;
            OnConnected = null;
            OnFailed = null;
            OnTimeOut = null;
        }
    }
}
