using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MessagePack;
using UnityEngine;

namespace NiuMa
{
    /// <summary>
    /// WebGL(H5) 用的 WebSocket 传输
    ///
    /// 浏览器没有裸 TCP，服务端另外开了 WebSocket 端口（默认 9098），
    /// 协议与 TCP 完全一致（msgpack 的 MsgWrapper 帧）。
    /// 收发都由 niuma-net.jslib 里的浏览器 WebSocket 完成，这里只做：
    ///   BeginConnect → jslib 建连接
    ///   Update       → 轮询状态 + 取回消息（base64）→ 解包 → 抛给 MsgManager
    ///   SendMessage  → 序列化后 base64 交给 jslib 发送（二进制帧）
    /// </summary>
    public class WebSocketConnection : Connection
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void NiuMa_WsConnect(string url);

        [DllImport("__Internal")]
        private static extern int NiuMa_WsState();

        [DllImport("__Internal")]
        private static extern void NiuMa_WsSend(string base64);

        [DllImport("__Internal")]
        private static extern IntPtr NiuMa_WsPoll();

        [DllImport("__Internal")]
        private static extern void NiuMa_WsClose();
#endif

        /// <summary>服务端 WebSocket 端口（与 server.ini 的 [Websocket] port 一致）</summary>
        private const int WS_PORT = 9098;

        private bool _open = false;
        private bool _closed = false;
        private readonly List<byte[]> _pending = new List<byte[]>();

        public override bool IsConnected()
        {
            return _open;
        }

        public override void BeginConnect(string ip, int port)
        {
            _open = false;
            _closed = false;
            _pending.Clear();
#if UNITY_WEBGL && !UNITY_EDITOR
            string url = "ws://" + ip + ":" + WS_PORT + "/";
            Debug.Log("[Net] WebSocket connecting: " + url);
            NiuMa_WsConnect(url);
#else
            Debug.LogWarning("[Net] WebSocketConnection 仅用于 WebGL");
#endif
        }

        public override void Close()
        {
            _open = false;
            _closed = true;
#if UNITY_WEBGL && !UNITY_EDITOR
            try { NiuMa_WsClose(); } catch (Exception e) { Debug.LogWarning("[Net] ws close: " + e.Message); }
#endif
        }

        public override void OnConnect(bool success, string errMsg)
        {
            var msg = new MsgConnect();
            msg.succeed = success ? 1 : 0;
            msg.errMsg = errMsg;
            MsgManager.Instance.PushMessage(MsgWrapper.PackMessage(msg));
        }

        public override void OnDisconnect(string err)
        {
            _open = false;
            _closed = true;
            var msg = new MsgDisconnect();
            msg.msg = err;
            MsgManager.Instance.PushMessage(MsgWrapper.PackMessage(msg));
        }

        public override void OnConnectFailed()
        {
            _open = false;
            _closed = true;
        }

        public override bool IsActiveClose()
        {
            return _closed;
        }

        public override void SendMessage(MsgWrapper mw)
        {
            if (!_open || mw == null)
                return;
            try
            {
                byte[] buf = MessagePackSerializer.Serialize<MsgWrapper>(mw);
                if (buf == null || buf.Length == 0)
                    return;
#if UNITY_WEBGL && !UNITY_EDITOR
                NiuMa_WsSend(Convert.ToBase64String(buf));
#else
                // 非 WebGL 环境不该走到这里
                Debug.LogWarning("[Net] WebSocketConnection.SendMessage 仅在 WebGL 可用");
#endif
            }
            catch (Exception ex)
            {
                Debug.LogError("[Net] WS 发送失败: " + ex.Message);
            }
        }

        public override void Update()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            int state = NiuMa_WsState(); // 0=connecting 1=open 2=closed 3=error
            if (!_open && !_closed && state == 1)
            {
                _open = true;
                Debug.Log("[Net] WebSocket connected");
                OnConnect(true, null);
            }
            else if (_open && state >= 2)
            {
                Debug.LogWarning("[Net] WebSocket closed by peer, state=" + state);
                OnDisconnect("WebSocket closed.");
            }
            else if (!_open && !_closed && state >= 2)
            {
                _closed = true;
                OnConnect(false, "WebSocket connect failed.");
            }

            // 拉取所有待处理消息
            for (int i = 0; i < 64; i++)
            {
                IntPtr ptr = NiuMa_WsPoll();
                if (ptr == IntPtr.Zero)
                    break;
                string b64 = Marshal.PtrToStringAnsi(ptr);
                if (string.IsNullOrEmpty(b64))
                    break;
                byte[] data = Convert.FromBase64String(b64);
                Unpack(data);
            }
#endif
        }

        /// <summary>把一段字节按 msgpack 帧解出来（与 TCP 版 UnpackMessage 等价）</summary>
        private void Unpack(byte[] data)
        {
            if (data == null || data.Length == 0)
                return;
            int offset = 0;
            int remain = data.Length;
            while (remain > 0)
            {
                try
                {
                    int consumed = 0;
                    var mem = new ReadOnlyMemory<byte>(data, offset, remain);
                    MsgWrapper mw = MessagePackSerializer.Deserialize<MsgWrapper>(mem, out consumed);
                    if (mw == null || consumed <= 0)
                        break;
                    offset += consumed;
                    remain -= consumed;
                    MsgManager.Instance.PushMessage(mw);
                }
                catch (Exception ex)
                {
                    Debug.LogError("[Net] WS 解包失败: " + ex.Message);
                    break;
                }
            }
        }
    }
}
