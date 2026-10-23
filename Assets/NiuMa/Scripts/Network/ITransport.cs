namespace NiuMa
{
    /// <summary>
    /// 传输层抽象：屏蔽 TCP 与 WebSocket 的差异
    ///
    /// 设计背景：
    ///   Unity WebGL 运行在浏览器沙箱里，**禁止使用原生 Socket**，
    ///   而 NiuMa 客户端原来直接 new Socket(TCP) 连接对局服。
    ///   为了让同一套游戏代码既能跑原生（Windows/Android/iOS），
    ///   又能跑 WebGL（浏览器），必须把「怎么传字节」与「传什么」解耦。
    ///
    /// 设计原则：
    ///   1. 上层（Connection / Session / 业务 / Lua）**完全不知道**底层是 TCP 还是 WS
    ///   2. 新增传输方式（如后续要加 KCP、QUIC）只需实现本接口，
    ///      **不需要修改 Connection**，避免越改越乱
    ///   3. 事件回调一律在 Unity 主线程触发，与原有实现保持一致，
    ///      业务侧无需感知线程差异
    ///
    /// 重要：服务端 TCP(10086) 与 WS(9098) **共用同一个 Session**，
    /// 帧格式都是裸 MessagePack 流（无自定义长度头），因此两侧行为天然一致。
    /// </summary>
    public interface ITransport
    {
        /// <summary>当前是否已连接</summary>
        bool IsConnected { get; }

        /// <summary>
        /// 发起连接
        /// </summary>
        /// <param name="host">主机（IP 或域名；生产环境建议用域名以便 TLS 证书匹配）</param>
        /// <param name="port">端口</param>
        void BeginConnect(string host, int port);

        /// <summary>
        /// 发送一段完整字节
        ///
        /// 调用方保证入参是**一条或多条完整 MessagePack 消息**，
        /// 传输层不做任何拆包/粘包处理（服务端用 msgpack 流式解包，天然支持）。
        /// </summary>
        void Send(byte[] data, int offset, int count);

        /// <summary>主动关闭连接</summary>
        void Close();

        /// <summary>连接成功（主线程）</summary>
        event System.Action OnConnected;

        /// <summary>
        /// 收到数据（主线程）
        ///
        /// 参数为「本次到达的字节」，可能需要多次回调才能凑齐一条消息；
        /// 上层 UnpackMessage 已支持粘包/半包处理，传输层只需如实投递。
        /// </summary>
        event System.Action<byte[], int> OnData;

        /// <summary>连接断开（主线程）。参数为原因，可为 null</summary>
        event System.Action<string> OnClosed;

        /// <summary>连接失败（主线程）。参数为错误信息</summary>
        event System.Action<string> OnConnectFailed;
    }
}
