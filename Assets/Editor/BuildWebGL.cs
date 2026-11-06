// 仅用于构建 H5（WebGL）包的编辑器脚本，不参与游戏运行逻辑。
// 用法（命令行）：
//   Unity.exe -quit -batchmode -projectPath <项目> -executeMethod NiuMa.Editor.BuildWebGL.Build -logFile -
// 产物：Builds/webgl
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace NiuMa.Editor
{
    public static class BuildWebGL
    {
        /// <summary>命令行入口：先打 WebGL 资源包 + Lua 包，再出 player</summary>
        public static void BuildAll()
        {
            AssetBundleBuilder.BuildWebGLResources();
            AssetBundleBuilder.BuildWebGLLuaScripts();
            Build();
        }

        [MenuItem("NiuMa/Build WebGL (H5)")]
        public static void Build()
        {
            string outDir = Path.Combine(Directory.GetCurrentDirectory(), "Builds", "webgl");
            Directory.CreateDirectory(outDir);

            // 场景列表沿用 EditorBuildSettings，不在这里硬编码
            string[] scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                Debug.LogError("[BuildWebGL] EditorBuildSettings 里没有启用的场景");
                EditorApplication.Exit(2);
                return;
            }

            Debug.Log("[BuildWebGL] 场景: " + string.Join(", ", scenes));

            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.WebGL.template = "APPLICATION:Default";

            BuildPlayerOptions opts = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outDir,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(opts);
            BuildSummary summary = report.summary;
            Debug.LogFormat("[BuildWebGL] result={0} size={1} errors={2} warnings={3}",
                summary.result, summary.totalSize, summary.totalErrors, summary.totalWarnings);

            EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
