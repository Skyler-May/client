using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace NiuMa
{
    /// <summary>
    /// H5 免登录入口（聚合平台下发）
    ///
    /// 平台 /api/v1/casino/niuma/launch 返回的 launchUrl 形如：
    ///   http://host/?mode=merchant&userData=AES(JSON{username,password,token})
    /// 这里解析 userData、取厂商 token，直接进入大厅，跳过自建登录/验证码。
    /// 非 WebGL 平台退回 Application.absoluteURL（通常没有 query，直接返回 false）。
    /// </summary>
    public static class MerchantLaunch
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern IntPtr NiuMa_GetLocationSearch();

        [DllImport("__Internal")]
        private static extern void NiuMa_InstallExitBeacon(string exitToken, string url);

        [DllImport("__Internal")]
        private static extern void NiuMa_InstallDebugOverlay();
#endif

        public static bool TryAutoLogin(Login login)
        {
            if (login == null)
                return false;

            string search = GetLocationSearch();
            if (string.IsNullOrEmpty(search))
                return false;

            string mode = GetQueryParam(search, "mode");
            string userData = GetQueryParam(search, "userData");
            if (string.IsNullOrEmpty(mode) || !mode.Equals("merchant", StringComparison.OrdinalIgnoreCase))
                return false;
            if (string.IsNullOrEmpty(userData))
            {
                Debug.LogWarning("[Merchant] 缺少 userData 参数");
                return false;
            }

            Debug.Log("[Merchant] userData 长度=" + userData.Length);

            string json = null;
            try
            {
                // 厂商用「登录载荷」那套密钥加密 userData（AesUtil.Encrypt/Decrypt）
                json = AesUtil.Decrypt(userData);
            }
            catch (Exception ex)
            {
                Debug.LogError("[Merchant] userData 解密失败: " + ex.Message);
                return false;
            }
            if (string.IsNullOrEmpty(json))
            {
                Debug.LogError("[Merchant] userData 解密结果为空");
                return false;
            }

            string token = ExtractField(json, "token");
            if (string.IsNullOrEmpty(token))
            {
                Debug.LogError("[Merchant] userData 中没有 token: " + json);
                return false;
            }

            // debug-only: ?autogame=GuanDan starts this game hall right after login
            string autoGame = GetQueryParam(search, "autogame");
            if (!string.IsNullOrEmpty(autoGame))
            {
                GameManager.AutoGame = autoGame;
                string autoRoom = GetQueryParam(search, "autoroom");
                if (!string.IsNullOrEmpty(autoRoom))
                {
                    GameManager.AutoRoom = autoRoom;
                }
                Debug.Log("[Merchant] auto game: " + autoGame + " room: " + (autoRoom ?? ""));
            }

            Debug.Log("[Merchant] login ok, entering hall");
            InstallExitBeacon(GetQueryParam(search, "exitToken"));
            InstallDebugOverlay(search);
        login.OnMerchantToken(token);
            return true;
        }

        /// <summary>URL 带 debug=1 时挂页面内调试浮层（联调用，生产不传即可）</summary>
        private static void InstallDebugOverlay(string search)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (string.IsNullOrEmpty(search) || search.IndexOf("debug=1", StringComparison.Ordinal) < 0)
                return;
            try
            {
                NiuMa_InstallDebugOverlay();
                Debug.Log("[Merchant] 调试浮层已开启");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Merchant] 调试浮层开启失败: " + ex.Message);
            }
#endif
        }

        /// <summary>
        /// 注册「关页即结算」：H5 关闭/刷新时用 sendBeacon 回调平台 /exit-by-token，
        /// 平台侧会把厂商余额退回主钱包。失败也没关系——平台 10 分钟兜底对账会补。
        /// </summary>
        private static void InstallExitBeacon(string exitToken)
        {
            if (string.IsNullOrEmpty(exitToken))
            {
                Debug.LogWarning("[Merchant] 没有 exitToken，关闭页面时依赖平台兜底对账");
                return;
            }
#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                string abs = Application.absoluteURL;
                string origin = "";
                int schemeEnd = abs.IndexOf("://");
                if (schemeEnd > 0)
                {
                    int pathStart = abs.IndexOf('/', schemeEnd + 3);
                    origin = pathStart > 0 ? abs.Substring(0, pathStart) : abs;
                }
                string url = origin + "/api/v1/casino/niuma/exit-by-token";
                NiuMa_InstallExitBeacon(exitToken, url);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Merchant] 注册退出结算失败: " + ex.Message);
            }
#endif
        }

        /// <summary>WebGL 下由 jslib 提供原生 location.search</summary>
        private static string GetLocationSearch()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                IntPtr p = NiuMa_GetLocationSearch();
                if (p == IntPtr.Zero)
                    return null;
                return Marshal.PtrToStringAnsi(p);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Merchant] 读取 location.search 失败: " + ex.Message);
                return null;
            }
#else
            string abs = Application.absoluteURL;
            if (string.IsNullOrEmpty(abs))
                return null;
            int q = abs.IndexOf('?');
            return q >= 0 ? abs.Substring(q) : null;
#endif
        }

        /// <summary>从 "?a=1&amp;b=2" 中取参数（只解 %XX，不把 '+' 变成空格）</summary>
        private static string GetQueryParam(string search, string key)
        {
            string query = search.StartsWith("?") ? search.Substring(1) : search;
            string[] parts = query.Split('&');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i];
                int eq = p.IndexOf('=');
                if (eq <= 0)
                    continue;
                string k = p.Substring(0, eq);
                if (!k.Equals(key, StringComparison.OrdinalIgnoreCase))
                    continue;
                string v = p.Substring(eq + 1);
                try
                {
                    return Uri.UnescapeDataString(v);
                }
                catch
                {
                    return v;
                }
            }
            return null;
        }

        /// <summary>极简 JSON 取值（只处理扁平的字符串字段）</summary>
        private static string ExtractField(string json, string field)
        {
            string key = "\"" + field + "\"";
            int i = json.IndexOf(key, StringComparison.Ordinal);
            if (i < 0)
                return null;
            int colon = json.IndexOf(':', i + key.Length);
            if (colon < 0)
                return null;
            int start = json.IndexOf('"', colon + 1);
            if (start < 0)
                return null;
            int end = json.IndexOf('"', start + 1);
            if (end < 0)
                return null;
            return json.Substring(start + 1, end - start - 1);
        }
    }
}
