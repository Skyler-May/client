using UnityEditor;
using UnityEngine;

/// <summary>
/// WebGL AssetBundle 命令行打包入口
///
/// 用途：让打包脱离 Unity 图形界面，可由 batchmode 触发。
/// 打包内容与菜单「Assets/AssetBundleBuilder/WebGL Resources」完全一致。
///
/// 用法：
///   Unity.exe -quit -batchmode -projectPath &lt;项目&gt; \
///     -executeMethod BuildWebGLAssetBundles.BuildAll -logFile &lt;日志&gt;
/// </summary>
public static class BuildWebGLAssetBundles
{
    [MenuItem("NiuMa/Build WebGL AssetBundles (CLI)")]
    public static void BuildAll()
    {
        try
        {
            // 1) 资源包（贴图/prefab/音频等，1442 个已标记 bundle 的资产）
            Debug.Log("[BuildAB] Building WebGL Resources...");
            AssetBundleBuilder.BuildWebGLResources();

            // 2) Lua 脚本包
            Debug.Log("[BuildAB] Building WebGL LuaScripts...");
            AssetBundleBuilder.BuildWebGLLuaScripts();

            Debug.Log("[BuildAB] DONE");
            EditorApplication.Exit(0);
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[BuildAB] Exception: " + ex);
            EditorApplication.Exit(1);
        }
    }
}
