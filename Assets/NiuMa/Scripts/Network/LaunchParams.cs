using System;
using UnityEngine;

namespace NiuMa
{
    /// <summary>
    /// 聚合平台启动参数（WebGL 专用）
    ///
    /// 定位：
    ///   Unity 客户端同时服务两种运行方式：
    ///     ① 独立运营：玩家自己输账号密码登录（不传任何启动参数）
    ///     ② 聚合平台：由平台后端签发 token，通过 URL 参数带入，
    ///                 **免登录直达指定游戏**（不经过大厅）
    ///
    /// URL 形式（与 NiuMa /vendor/launch 返回的 launchUrl 一致）：
    ///   https://h5.example.com/index.html?token=Bearer%20xxx&game=GUANDAN
    ///
    /// 安全说明：
    ///   token 只在内存使用，不写入 PlayerPrefs（WebGL 的 localStorage 可被读取）；
    ///   用完不清 URL（WebGL 下修改地址栏会导致页面刷新，风险大于收益），
    ///   生产环境应依赖 HTTPS + 短时效 token。
    /// </summary>
    public static class LaunchParams
    {
        /// <summary>平台签发的玩家令牌（含 "Bearer " 前缀）</summary>
        public static string Token { get; private set; }

        /// <summary>目标游戏编码（如 GUANDAN / LACKEY / MAHJONG）</summary>
        public static string Game { get; private set; }

        /// <summary>是否解析过（避免重复解析）</summary>
        private static bool _parsed = false;

        /// <summary>是否为聚合平台启动（有 token 才算）</summary>
        public static bool IsAggregated
        {
            get { EnsureParsed(); return !string.IsNullOrEmpty(Token); }
        }

        /// <summary>是否要求直达某个游戏</summary>
        public static bool HasTargetGame
        {
            get { EnsureParsed(); return !string.IsNullOrEmpty(Game); }
        }

        private static void EnsureParsed()
        {
            if (_parsed) return;
            _parsed = true;

#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                string url = Application.absoluteURL;
                if (string.IsNullOrEmpty(url)) return;

                int q = url.IndexOf('?');
                if (q < 0) return;

                string query = url.Substring(q + 1);
                // 去掉 #fragment（若有）
                int hash = query.IndexOf('#');
                if (hash >= 0) query = query.Substring(0, hash);

                foreach (string pair in query.Split('&'))
                {
                    if (string.IsNullOrEmpty(pair)) continue;
                    int eq = pair.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = pair.Substring(0, eq);
                    string val = Uri.UnescapeDataString(pair.Substring(eq + 1));
                    if (key == "token") Token = val;
                    else if (key == "game") Game = val;
                }

                if (!string.IsNullOrEmpty(Token))
                    Debug.Log("聚合平台启动：game=" + Game + "，已获取 token");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("解析启动参数失败: " + ex.Message);
            }
#endif
        }

        /// <summary>
        /// 由外部（如 Lua）显式设置（供调试或特殊渠道使用）
        /// </summary>
        public static void Set(string token, string game)
        {
            _parsed = true;
            Token = token;
            Game = game;
        }
    }
}
