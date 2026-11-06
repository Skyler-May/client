using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using XLua;

namespace NiuMa
{
    public class LuaManager
    {
        private LuaManager() {
            Initialize();
        }
        private static readonly LuaManager _instance = new LuaManager();

        public static LuaManager Instance
        {
            get { return _instance; }
        }

        private LuaEnv _lua = null;
        private static float lastGCTime = 0;
        private const float GCInterval = 1; // 1 second 

        public LuaEnv Lua
        {
            get
            {
                return _lua;
            }
        }

        private void Initialize()
        {
            _lua = new LuaEnv();
            _lua.AddLoader(LuaScriptLoader.ReadFile);
            // WebGL 没有 rapidjson 原生模块，改用 LuaScripts/rapidjson.lua 的纯 Lua 实现
#if !UNITY_WEBGL
            _lua.AddBuildin("rapidjson", XLua.LuaDLL.Lua.LoadRapidJson);
#endif
        }

        public void Update(float time)
        {
            if (time - lastGCTime > GCInterval)
            {
                _lua.Tick();
                lastGCTime = time;
            }
        }

        public void Close()
        {
            //_lua.Dispose();
            //_lua = null;
        }
    }
}