using System;
using System.Collections.Generic;
using UnityEngine;

namespace NiuMa
{
    /// <summary>
    /// WebSocket 消息接收器
    ///
    /// 为什么需要它：
    ///   Unity 的 jslib → C# 只能通过 `SendMessage(gameObjectName, method, arg)`
    ///   调用**挂在 GameObject 上的组件方法**，不能直接调用普通 C# 对象。
    ///   因此需要一个 MonoBehaviour 做中转，把 JS 回调转给 WebSocketTransport。
    ///
    /// 生命周期：
    ///   Connection 建立 WebGL 连接时确保接收器存在（DontDestroyOnLoad），
    ///   连接断开后不销毁，供重连复用。
    /// </summary>
    public class WebSocketReceiver : MonoBehaviour
    {
        private static WebSocketReceiver _instance;

        /// 当前活动的传输实例（同一时刻只支持一个对局连接）
        private static WebSocketTransport _transport;

        private readonly List<string[]> _pending = new List<string[]>();
        private bool _dispatching = false;

        /// <summary>接收器 GameObject 名称（jslib 用它定位）</summary>
        public const string GameObjectName = "NiuMaWebSocketReceiver";

        /// <summary>
        /// 取得（必要时创建）全局接收器
        /// </summary>
        public static WebSocketReceiver Ensure()
        {
            if (_instance != null)
                return _instance;

            GameObject go = GameObject.Find(GameObjectName);
            if (go == null)
            {
                go = new GameObject(GameObjectName);
                DontDestroyOnLoad(go);
            }
            _instance = go.GetComponent<WebSocketReceiver>();
            if (_instance == null)
                _instance = go.AddComponent<WebSocketReceiver>();
            return _instance;
        }

        public static void Bind(WebSocketTransport transport)
        {
            _transport = transport;
        }

        public static void Unbind(WebSocketTransport transport)
        {
            if (ReferenceEquals(_transport, transport))
                _transport = null;
        }

        // ============================================================
        // 以下由 jslib SendMessage 调用
        // 注意：SendMessage 只能传单个 string 参数，复杂数据需自行编码
        // ============================================================

        /// <summary>格式：handle</summary>
        private void OnWebSocketConnected(string arg)
        {
            int handle;
            if (!int.TryParse(arg, out handle)) return;
            if (_transport != null)
                _transport.HandleOpen(handle);
        }

        /// <summary>格式：handle|ptr|length</summary>
        private void OnWebSocketMessage(string arg)
        {
            if (string.IsNullOrEmpty(arg)) return;
            string[] parts = arg.Split('|');
            if (parts.Length < 3) return;

            int handle, ptr, len;
            if (!int.TryParse(parts[0], out handle)) return;
            if (!int.TryParse(parts[1], out ptr)) return;
            if (!int.TryParse(parts[2], out len)) return;

            byte[] payload = ReadAndFree(ptr, len);
            if (payload == null) return;

            if (_transport != null)
                _transport.HandleMessage(payload, len);
        }

        /// <summary>格式：handle|reason</summary>
        private void OnWebSocketClosed(string arg)
        {
            int handle = -1;
            string reason = null;
            if (!string.IsNullOrEmpty(arg))
            {
                int idx = arg.IndexOf('|');
                if (idx > 0)
                {
                    int.TryParse(arg.Substring(0, idx), out handle);
                    reason = arg.Substring(idx + 1);
                }
                else
                {
                    int.TryParse(arg, out handle);
                }
            }
            if (_transport != null)
                _transport.HandleClose(handle, reason);
        }

        /// <summary>格式：错误描述</summary>
        private void OnWebSocketError(string arg)
        {
            if (_transport != null)
                _transport.HandleError(arg);
        }

        // ============================================================
        // WASM 堆读取
        // ============================================================

#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal", EntryPoint = "NiuMaWS_Free")]
        private static extern void NiuMaWS_Free(int ptr);

        private static byte[] ReadAndFree(int ptr, int len)
        {
            if (ptr <= 0 || len <= 0)
                return null;
            byte[] result = new byte[len];
            try
            {
                System.Runtime.InteropServices.Marshal.Copy((IntPtr)ptr, result, 0, len);
            }
            finally
            {
                // 无论拷贝是否成功都要释放，避免 WASM 堆泄漏
                NiuMaWS_Free(ptr);
            }
            return result;
        }
#else
        private static byte[] ReadAndFree(int ptr, int len)
        {
            Debug.LogError("WebSocketReceiver 仅支持 WebGL 平台");
            return null;
        }
#endif
    }
}
