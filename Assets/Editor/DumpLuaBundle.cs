using System.IO;
using UnityEditor;
using UnityEngine;

namespace NiuMa.Editor
{
    /// <summary>
    /// 从已构建的 luascripts.ab 里把 Lua 文本导出到磁盘（用于恢复误改的脚本）
    /// </summary>
    public static class DumpLuaBundle
    {
        [MenuItem("NiuMa/Dump Lua Bundle")]
        public static void Dump()
        {
            string bundlePath = "AssetBundle/webgl/luascripts.ab";
            string outDir = "../.dsh-acl-report/lua-dump";
            Directory.CreateDirectory(outDir);
            AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);
            if (bundle == null)
            {
                Debug.LogError("[DumpLuaBundle] 打不开 " + bundlePath);
                return;
            }
            Object[] objs = bundle.LoadAllAssets();
            int n = 0;
            foreach (Object o in objs)
            {
                TextAsset ta = o as TextAsset;
                if (ta == null)
                    continue;
                string name = (n + 1).ToString("D3") + "_" + ta.name.Replace('/', '_').Replace('\\', '_');
                File.WriteAllText(Path.Combine(outDir, name + ".txt"), ta.text);
                n++;
            }
            Debug.Log("[DumpLuaBundle] 导出 " + n + " 个文本资源到 " + Path.GetFullPath(outDir));
            bundle.Unload(true);
        }
    }
}
