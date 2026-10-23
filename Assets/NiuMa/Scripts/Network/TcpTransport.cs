using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

namespace NiuMa
{
    /// <summary>
    /// TCP 传输实现（原生平台使用：Windows / Android / iOS / macOS）
    ///
    /// 本类是原有 Connection.cs 里 Socket 逻辑的**原样搬迁**，
    /// 行为、超时、缓冲区大小全部保持一致，仅把回调改为事件形式，
    /// 以确保原生端**零回归**。
    ///
    /// WebGL 平台不会编译本类（见 WebSocketTransport）。
    /// </summary>
    public class TcpTransport : ITransport
    {
        private Socket _socket = null;
        private bool _connected = false;

        /// 接收缓冲区长度（与 Connection.RECEIVE_LENGTH 保持一致）
        public const int RECEIVE_LENGTH = 8192;

        public bool IsConnected { get { return _connected; } }

        public event Action OnConnected;
        public event Action<byte[], int> OnData;
        public event Action<string> OnClosed;
        public event Action<string> OnConnectFailed;

        public void BeginConnect(string host, int port)
        {
            try
            {
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                _socket.Blocking = false;
                _socket.ReceiveTimeout = 3000;
                _socket.SendTimeout = 3000;

#if (UNITY_EDITOR || UNITY_STANDALONE || UNITY_IOS)
                _socket.SetSocketOption(System.Net.Sockets.SocketOptionLevel.Socket,
                    SocketOptionName.ReceiveBuffer, RECEIVE_LENGTH);
                _socket.SetSocketOption(System.Net.Sockets.SocketOptionLevel.Socket,
                    SocketOptionName.NoDelay, true);
#elif UNITY_ANDROID
                _socket.ReceiveBufferSize = RECEIVE_LENGTH;
                _socket.NoDelay = true;
#endif

                Debug.Log("Begin Connect (TCP): " + host + ":" + port);

                // 支持域名：WebGL 走域名后，为保持两端配置一致，这里也允许域名
                IPAddress addr;
                IPEndPoint ep;
                if (IPAddress.TryParse(host, out addr))
                {
                    ep = new IPEndPoint(addr, port);
                }
                else
                {
                    IPAddress[] list = Dns.GetHostAddresses(host);
                    if (list == null || list.Length == 0)
                    {
                        Raise(OnConnectFailed, "域名解析失败: " + host);
                        return;
                    }
                    ep = new IPEndPoint(list[0], port);
                }

                _socket.BeginConnect(ep, ConnectCallBack, this);
            }
            catch (Exception ex)
            {
                Raise(OnConnectFailed, "Connect to server error: " + ex.Message);
            }
        }

        private static void ConnectCallBack(IAsyncResult ar)
        {
            TcpTransport self = ar.AsyncState as TcpTransport;
            if (self == null)
                return;

            bool success = false;
            string errMsg = null;
            try
            {
                if (self._socket != null)
                    self._socket.EndConnect(ar);
                if (ar.IsCompleted)
                    success = true;
            }
            catch (Exception ex)
            {
                errMsg = "Connect to server error: " + ex.Message;
            }

            // 回调统一投递到主线程，与原实现一致
            UnityMainThreadDispatcher.Enqueue(() =>
            {
                if (success)
                {
                    self._connected = true;
                    Debug.Log("Connect to server success (TCP)");
                    if (self.OnConnected != null) self.OnConnected();
                    self.BeginReceive();
                }
                else
                {
                    Debug.LogError(errMsg);
                    if (self.OnConnectFailed != null) self.OnConnectFailed(errMsg);
                }
            });
        }

        private byte[] _recvBuffer = new byte[RECEIVE_LENGTH];

        private void BeginReceive()
        {
            if (_socket == null || !_connected)
                return;
            try
            {
                _socket.BeginReceive(_recvBuffer, 0, _recvBuffer.Length, SocketFlags.None,
                    ReceiveCallBack, this);
            }
            catch (Exception ex)
            {
                Disconnect("BeginReceive error: " + ex.Message);
            }
        }

        private static void ReceiveCallBack(IAsyncResult ar)
        {
            TcpTransport self = ar.AsyncState as TcpTransport;
            if (self == null)
                return;

            int readCount = 0;
            string errMsg = null;
            try
            {
                if (self._socket != null)
                    readCount = self._socket.EndReceive(ar);
            }
            catch (Exception ex)
            {
                errMsg = "Receive error: " + ex.Message;
            }

            UnityMainThreadDispatcher.Enqueue(() =>
            {
                if (errMsg != null)
                {
                    self.Disconnect(errMsg);
                    return;
                }
                if (readCount <= 0)
                {
                    // 对端关闭
                    self.Disconnect(null);
                    return;
                }

                // 拷贝一份再投递，避免下次 BeginReceive 覆盖
                byte[] copy = new byte[readCount];
                Buffer.BlockCopy(self._recvBuffer, 0, copy, 0, readCount);
                if (self.OnData != null) self.OnData(copy, readCount);

                self.BeginReceive();
            });
        }

        public void Send(byte[] data, int offset, int count)
        {
            if (_socket == null || !_connected || data == null || count <= 0)
                return;
            try
            {
                _socket.BeginSend(data, offset, count, SocketFlags.None, SendCallBack, this);
            }
            catch (Exception ex)
            {
                Disconnect("Send error: " + ex.Message);
            }
        }

        private static void SendCallBack(IAsyncResult ar)
        {
            TcpTransport self = ar.AsyncState as TcpTransport;
            if (self == null)
                return;
            try
            {
                if (self._socket != null)
                    self._socket.EndSend(ar);
            }
            catch (Exception ex)
            {
                UnityMainThreadDispatcher.Enqueue(() => self.Disconnect("EndSend error: " + ex.Message));
            }
        }

        public void Close()
        {
            if (_socket == null)
                return;
            try
            {
                if (_connected)
                    _socket.Shutdown(SocketShutdown.Both);
            }
            catch { }
            try
            {
                _socket.Close();
            }
            catch { }
            _socket = null;
            _connected = false;
        }

        private void Disconnect(string reason)
        {
            _connected = false;
            Close();
            if (OnClosed != null) OnClosed(reason);
        }

        private void Raise(Action<string> handler, string msg)
        {
            UnityMainThreadDispatcher.Enqueue(() =>
            {
                if (handler != null) handler(msg);
            });
        }
    }
}
