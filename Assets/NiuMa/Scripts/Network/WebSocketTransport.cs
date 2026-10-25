using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace NiuMa
{
    /// <summary>
    /// WebSocket 传输实现（仅在 WebGL 平台编译启用）
    ///
    /// 选择 WS 而不是 ClientWebSocket 的原因：
    ///   1. Unity WebGL 的 .NET 运行时是精简版，ClientWebSocket 支持不完整；
    ///   2. 浏览器原生 WebSocket 最稳定，且自动复用浏览器的 TLS/代理配置；
    ///   3. wss 证书校验交给浏览器，客户端无需自行处理证书链。
    ///
    /// 服务端支持：
    ///   NiuMa 的 C++ 服务端（boost::beast）同时提供 TCP 10086 与 WS 9098，
    ///   两者**共用同一个 Session**、同一套 MessagePack 帧格式，
    ///   因此本类只需负责搬运字节，不做任何协议适配。
    ///
    /// 线程模型：
    ///   WebGL 不支持多线程，本类完全事件驱动（浏览器回调 → SendMessage → C#），
    ///   不创建任何 Thread，与 TcpTransport 的多线程实现无关。
    /// </summary>
    public class WebSocketTransport : ITransport
    {
        private int _handle = -1;
        private bool _connected = false;

        /// <summary>接收消息回调的 GameObject 名（需挂 WebSocketReceiver）</summary>
        private readonly string _receiverName;

        public bool IsConnected { get { return _connected; } }

        public event Action OnConnected;
        public event Action<byte[], int> OnData;
        public event Action<string> OnClosed;
        public event Action<string> OnConnectFailed;

        public WebSocketTransport(string receiverGameObjectName)
        {
            _receiverName = receiverGameObjectName;
        }

        /// <summary>
        /// 把服务端下发的 TCP 端口映射为 WebSocket 端口
        ///
        /// 背景：服务端下发 venue 地址时给的是 TCP 端口（默认 10086），
        /// 但浏览器只能用 WebSocket 端口（默认 9098）。两者是同一 Session、
        /// 同一 MessagePack 帧，因此只需换端口，无需服务端改协议。
        ///
        /// 生产建议：由 nginx 终结 TLS，监听 443 并把 wss 反代到内网 9098，
        /// 此时应通过环境配置把 WsPort 设为 443，同时 Ip 使用域名。
        /// </summary>
        public static int ResolveWsPort(string host, int tcpPort)
        {
            // 显式配置优先（部署时按实际情况设置）
            if (WsPortOverride > 0)
                return WsPortOverride;

            // 默认映射：TCP 10086 → WS 9098
            if (tcpPort == TcpGamePort)
                return DefaultWsPort;

            // 其他端口按原值（便于同机另开 WS 端口做灰度）
            return tcpPort;
        }

        /// <summary>服务端默认 TCP 对局端口</summary>
        public const int TcpGamePort = 10086;

        /// <summary>服务端默认 WebSocket 端口</summary>
        public const int DefaultWsPort = 9098;

        /// <summary>
        /// 强制指定 WS 端口（0 表示按默认映射规则）
        ///
        /// WebGL 启动时可根据部署配置设置，例如生产用 443（nginx TLS 终结）。
        /// </summary>
        public static int WsPortOverride = 0;

        public void BeginConnect(string host, int port)
        {
            string url = BuildUrl(host, port);
            Debug.Log("Begin Connect (WebSocket): " + url);
#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                _handle = NiuMaWS.Connect(url, _receiverName);
                if (_handle < 0)
                    RaiseFailed("WebSocket 创建失败: " + url);
            }
            catch (Exception ex)
            {
                RaiseFailed("WebSocket 异常: " + ex.Message);
            }
#else
            // 非 WebGL 平台不应实例化本类；兜底给出明确错误而不是静默失败
            RaiseFailed("WebSocketTransport 仅支持 WebGL 平台");
#endif
        }

        /// <summary>
        /// 构造 WS 地址
        ///
        /// 安全策略：
        ///  · 页面是 https 时**必须**用 wss，否则浏览器会以混合内容为由拒绝连接
        ///    （生产环境由 nginx/Caddy 终结 TLS，把 wss 反代成 ws 转发到 9098）
        ///  · 页面是 http（本地开发）时用 ws
        /// </summary>
        private string BuildUrl(string host, int port)
        {
            string scheme = "ws";
#if UNITY_WEBGL && !UNITY_EDITOR
            // Application.absoluteURL 形如 https://h5.example.com/index.html
            string page = Application.absoluteURL;
            if (!string.IsNullOrEmpty(page) && page.StartsWith("https", StringComparison.OrdinalIgnoreCase))
                scheme = "wss";
#endif
            return scheme + "://" + host + ":" + port;
        }

        public void Send(byte[] data, int offset, int count)
        {
            if (!_connected || data == null || count <= 0)
                return;
#if UNITY_WEBGL && !UNITY_EDITOR
            NiuMaWS.Send(_handle, data, offset, count);
#endif
        }

        public void Close()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (_handle >= 0)
            {
                NiuMaWS.Close(_handle);
                _handle = -1;
            }
#endif
            _connected = false;
        }

        // ============================================================
        // 以下方法由 jslib 通过 SendMessage 调用（WebSocketReceiver 转发）
        // ============================================================

        /// <summary>浏览器 onopen</summary>
        public void HandleOpen(int handle)
        {
            if (handle != _handle)
                return;
            _connected = true;
            Debug.Log("Connect to server success (WebSocket)");
            if (OnConnected != null) OnConnected();
        }

        /// <summary>浏览器 onmessage（二进制）</summary>
        public void HandleMessage(byte[] payload, int length)
        {
            if (!_connected)
                return;
            if (OnData != null) OnData(payload, length);
        }

        /// <summary>浏览器 onclose</summary>
        public void HandleClose(int handle, string reason)
        {
            if (handle != _handle)
                return;
            _connected = false;
            _handle = -1;
            if (OnClosed != null) OnClosed(reason);
        }

        /// <summary>浏览器 onerror</summary>
        public void HandleError(string message)
        {
            if (!_connected)
                RaiseFailed(message);
            else if (OnClosed != null)
                OnClosed(message);
        }

        private void RaiseFailed(string msg)
        {
            Debug.LogError(msg);
            if (OnConnectFailed != null) OnConnectFailed(msg);
        }
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    /// <summary>
    /// jslib 绑定声明（实现见 Plugins/WebGL/NiuMaWS.jslib）
    /// </summary>
    internal static class NiuMaWS
    {
        // EntryPoint 必须与 jslib 导出的函数名严格一致（NiuMaWS_ 前缀），
        // 否则 IL2CPP 链接阶段会报 undefined symbol: Connect / Send / Close
        [DllImport("__Internal", EntryPoint = "NiuMaWS_Connect")]
        public static extern int Connect(string url, string receiverGameObject);

        [DllImport("__Internal", EntryPoint = "NiuMaWS_Send")]
        public static extern void Send(int handle, byte[] data, int offset, int count);

        [DllImport("__Internal", EntryPoint = "NiuMaWS_Close")]
        public static extern void Close(int handle);
    }
#endif
}
