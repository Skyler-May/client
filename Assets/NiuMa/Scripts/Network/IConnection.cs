namespace NiuMa
{
    /// <summary>
    /// 网络连接抽象
    ///
    /// 原生平台（Android/iOS/PC）用裸 TCP（<see cref="Connection"/>），
    /// WebGL(H5) 用 WebSocket（<see cref="WebSocketConnection"/>）——浏览器没有裸 TCP。
    /// 两者都收发同一套 msgpack 消息（<see cref="MsgWrapper"/>）。
    /// </summary>
    public interface IConnection
    {
        bool IsConnected();

        void BeginConnect(string ip, int port);

        void Close();

        void OnConnect(bool success, string errMsg);

        void OnDisconnect(string err);

        void OnConnectFailed();

        bool IsActiveClose();

        void SendMessage(MsgWrapper mw);

        /// <summary>每帧驱动（WebSocket 需要轮询收发；TCP 实现为空）</summary>
        void Update();
    }
}
