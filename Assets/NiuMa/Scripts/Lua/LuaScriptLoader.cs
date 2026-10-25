using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace NiuMa
{
    public static class LuaScriptLoader
    {
        public static byte[] ReadFile(ref string fileName)
        {
            UnityEngine.Object obj = null;
            fileName = fileName.Replace('.', '/');
            if (!fileName.EndsWith(".lua"))
                fileName = fileName + ".lua";
#if UNITY_EDITOR
            string resName = "LuaScripts/" + fileName;
            obj = Resources.Load(resName);
#elif (UNITY_STANDALONE || UNITY_ANDROID || UNITY_IOS || UNITY_WEBGL)
            // WebGL 同样从 luascripts.ab 里读 Lua（该包已随基础包预加载进内存）
            obj = ResourceManager.Instance.LoadResource(fileName, "luascripts.ab");
#endif
            TextAsset text = obj as TextAsset;
            if (text != null)
                return text.bytes;
            return null;
        }
    }
}
