using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using MessagePack;

namespace NiuMa
{
    /// <summary>
    /// 对局服务器连接
    ///
    /// 职责：
    ///  1. 维持与会话服的一条连接（TCP 或 WebSocket，由平台决定）
    ///  2. MessagePack 编解码（粘包/半包处理）
    ///  3. 发送队列串行化（保证消息不交叉）
    ///
    /// 传输层解耦（2026-10-24 改造）：
    ///   原来直接 new Socket(TCP) 连接，导致 WebGL 平台不可用
    ///   （浏览器禁止原生 Socket）。现改为依赖 ITransport 接口：
    ///     · 原生平台 → TcpTransport
    ///     · WebGL 平台 → WebSocketTransport
    ///   编译期决定，运行期零分支。
    ///
    /// **业务零改动**：MessagePack 编解码、粘包处理、发送队列全部保持原逻辑。
    /// </summary>
    public class Connection
    {
        /// <summary>传输层实现（由平台决定）</summary>
        private ITransport _transport = null;

        /// <summary>
        /// 为兼容旧代码保留的 Socket 访问器
        ///
        /// 注意：WebGL 下没有 Socket，返回 null。
        /// 新代码不应依赖此属性，改用 IsConnected / Send。
        /// </summary>
        [Obsolete("请改用 IsConnected / Send。WebGL 平台无 Socket。")]
        public System.Net.Sockets.Socket Socket
        {
            get { return null; }
        }

        // 是否已经成功连接
        bool _connected = false;

        // 是否主动关闭
        bool _activeClose = false;

        // 线程锁（保护发送队列；WebGL 单线程下也无害）
        object _lock = new object();

        // 合并缓存（跨次接收的残留字节）
        private byte[] _bufferCompact = null;

        // 交换缓存
        private byte[] _bufferSwap = null;

        // 接收缓存长度
        public const int RECEIVE_LENGTH = 8192;

        // 解包数据缓存长度，固定为4MB
        public const int BUFFER_LENGTH = 4194304;

        // 打包缓存中已占用字节数
        private int _bufferCount = 0;

        // 网络发送节点
        private class SendNode
        {
            public byte[] _buf = null;
            public int _len = 0;
        }

        // 发送队列
        private Queue<SendNode> _sendingQueue = new Queue<SendNode>();

        // 是否正在发送
        private bool _sending = false;

        public bool IsConnected()
        {
            return _connected && _transport != null && _transport.IsConnected;
        }

        public void SetConnected(bool s)
        {
            _connected = s;
        }

        public bool IsActiveClose()
        {
            return _activeClose;
        }

        private void SetActiveClose(bool s)
        {
            _activeClose = s;
        }

        private void SetDisconnect()
        {
            _connected = false;
            lock (_lock)
            {
                _sending = false;
                _sendingQueue.Clear();
            }
        }

        public void Close()
        {
            if (IsActiveClose())
                return;
            SetActiveClose(true);
            if (_transport != null)
            {
                try
                {
                    _transport.Close();
                }
                catch { }
            }
        }

        /// <summary>
        /// 创建传输层实现
        ///
        /// WebGL 用 WebSocket（浏览器原生），其余平台用 TCP。
        /// 编译期决定，运行期无分支开销，也不会出现"某平台误用另一实现"。
        /// </summary>
        private ITransport CreateTransport()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            WebSocketReceiver.Ensure();
            return new WebSocketTransport(WebSocketReceiver.GameObjectName);
#else
            return new TcpTransport();
#endif
        }

        public void BeginConnect(string ip, int port)
        {
            SetActiveClose(false);

            _transport = CreateTransport();

            // ------------------------------------------------------------
            // WebGL 端口适配
            //
            // 服务端下发的 port 是 TCP 端口（10086），而浏览器只能用 WebSocket（9098）。
            // 二者是同一个 Session、同一套 MessagePack 帧，仅传输通道不同，
            // 因此这里做一次端口映射即可，不需要服务端下发两套地址。
            //
            // 生产环境由 nginx 终结 TLS：wss://域名/ws → ws://内网:9098，
            // 此时 ip 为域名、端口走 443（由部署配置决定，见 docs/webgl-h5-design.md）。
            // ------------------------------------------------------------
#if UNITY_WEBGL && !UNITY_EDITOR
            port = WebSocketTransport.ResolveWsPort(ip, port);
#endif

            _transport.OnConnected += HandleTransportConnected;
            _transport.OnData += HandleTransportData;
            _transport.OnClosed += HandleTransportClosed;
            _transport.OnConnectFailed += HandleTransportConnectFailed;

            _transport.BeginConnect(ip, port);
        }

        // ============================================================
        // 传输层事件处理（统一在主线程）
        // ============================================================

        private void HandleTransportConnected()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL：把接收器绑定到本次连接
            WebSocketReceiver.Bind(_transport as WebSocketTransport);
#endif
            SetConnected(true);
            Debug.Log("Connect to server success");

            // 与原有行为一致：连接成功也推一条 MsgConnect
            OnConnect(true, null);
        }

        private void HandleTransportData(byte[] data, int length)
        {
            OnReceiveData(data, length);
        }

        private void HandleTransportClosed(string reason)
        {
            OnDisconnect(reason);
        }

        private void HandleTransportConnectFailed(string errMsg)
        {
            OnConnect(false, errMsg);
        }

        // ============================================================
        // 业务侧接口（保持与改造前完全一致）
        // ============================================================

        public void OnConnect(bool success, string errMsg)
        {
            MsgConnect msg = new MsgConnect();
            msg.succeed = success ? 1 : 0;
            msg.errMsg = errMsg;
            MsgWrapper mw = MsgWrapper.PackMessage(msg);
            MsgManager.Instance.PushMessage(mw);
        }

        public void OnDisconnect(string err)
        {
            if (_transport == null)
                return;
            Close();
            _transport = null;
            if (!IsActiveClose())
                SetDisconnect();

            MsgDisconnect msg = new MsgDisconnect();
            msg.msg = err;
            MsgWrapper mw = MsgWrapper.PackMessage(msg);
            MsgManager.Instance.PushMessage(mw);
        }

        public void OnConnectFailed()
        {
            _transport = null;
        }

        // ============================================================
        // 数据接收与解包
        // ============================================================

        /// <summary>
        /// 收到一段数据（传输层已把数据拷贝出来，长度即有效字节数）
        /// </summary>
        private void OnReceiveData(byte[] data, int readCount)
        {
            UnpackMessage(data, readCount);
        }

        private void UnpackMessage(byte[] data, int readCount)
        {
            if (_bufferCompact == null)
                _bufferCompact = new byte[BUFFER_LENGTH];

            int totalCount = _bufferCount + readCount;
            if (totalCount > BUFFER_LENGTH)
            {
                // 缓冲区溢出
#if UNITY_EDITOR
                UnityMainThreadDispatcher.Enqueue(() =>
                {
                    Debug.LogError("网络接收缓冲区溢出");
                });
#endif
                Array.Clear(_bufferCompact, 0, BUFFER_LENGTH);
                _bufferCount = 0;
                return;
            }

            Buffer.BlockCopy(data, 0, _bufferCompact, _bufferCount, readCount);
            _bufferCount = totalCount;
            totalCount = 0;
            int tmpCount = 0;

            while (_bufferCount > 0)
            {
                try
                {
                    ReadOnlyMemory<byte> mem = new ReadOnlyMemory<byte>(_bufferCompact, totalCount, _bufferCount);
                    MsgWrapper mw = MessagePackSerializer.Deserialize<MsgWrapper>(mem, out tmpCount);
                    if (mw == null)
                        break;
                    totalCount += tmpCount;
                    _bufferCount -= tmpCount;
                    MsgManager.Instance.PushMessage(mw);
                }
                catch (Exception)
                {
                    // 数据不完整，等待后续字节（粘包/半包正常情况）
                    break;
                }
            }

            if (totalCount > 0 && _bufferCount > 0)
            {
                // 把数据移动到缓存开头
                if (_bufferSwap == null)
                    _bufferSwap = new byte[BUFFER_LENGTH];
                Buffer.BlockCopy(_bufferCompact, totalCount, _bufferSwap, 0, _bufferCount);
                // 将缓存引用交换
                byte[] buf = _bufferSwap;
                _bufferSwap = _bufferCompact;
                _bufferCompact = buf;
            }
        }

        // ============================================================
        // 发送
        // ============================================================

        private bool IsSending()
        {
            lock (_lock)
            {
                return _sending;
            }
        }

        private void SetSending(bool s)
        {
            lock (_lock)
            {
                _sending = s;
            }
        }

        private void PushSendingNode(SendNode node)
        {
            if (node == null || node._len <= 0)
                return;

            lock (_lock)
            {
                _sendingQueue.Enqueue(node);
            }
        }

        private SendNode PopSendingNode()
        {
            lock (_lock)
            {
                if (_sendingQueue.Count <= 0)
                    return null;

                return _sendingQueue.Dequeue();
            }
        }

        public void SendMessage(MsgWrapper mw)
        {
            if (!IsConnected())
                return;
            byte[] buf = null;
            try
            {
                buf = MessagePackSerializer.Serialize<MsgWrapper>(mw);
                if (buf == null || buf.Length == 0)
                {
                    Debug.LogError("Serialize MsgWrapper error.");
                    return;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogErrorFormat("Serialize MsgWrapper error: {0}.", ex.Message);
                return;
            }
            SendNode node = new SendNode();
            node._buf = buf;
            node._len = buf.Length;
            PushSendingNode(node);

            if (!IsSending())
                SendHeadNode();
        }

        /// <summary>
        /// 发送队列串行化
        ///
        /// 与原实现差异：TCP 异步回调会驱动下一条发送；
        /// 而 WebSocket 的 send 是同步入队（浏览器负责缓冲），
        /// 因此这里统一改为「同步逐条发出」，两种传输行为一致。
        /// </summary>
        private void SendHeadNode()
        {
            if (_transport == null)
            {
                SetSending(false);
                return;
            }

            SetSending(true);
            while (true)
            {
                SendNode node = PopSendingNode();
                if (node == null)
                    break;
                try
                {
                    _transport.Send(node._buf, 0, node._len);
                }
                catch (Exception ex)
                {
                    Debug.LogError("Send package failed: " + ex.Message);
                    OnDisconnect(ex.Message);
                    SetSending(false);
                    return;
                }
            }
            SetSending(false);
        }
    }
}
