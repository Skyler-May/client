using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// WebGL 命令行构建脚本
///
/// 用途：让构建脱离 Unity 图形界面，可由命令行（batchmode）触发，
/// 便于 CI / 自动化排查编译错误。
///
/// 用法：
///   Unity.exe -quit -batchmode -projectPath <项目> \
///     -executeMethod BuildWebGL.Build -logFile <日志>
/// </summary>
public static class BuildWebGL
{
    private const string OutputDir = @"D:\my-projects\niuma\webgl";

    [MenuItem("NiuMa/Build WebGL (CLI)")]
    public static void Build()
    {
        try
        {
            Directory.CreateDirectory(OutputDir);

            // 切换到 WebGL 平台
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
            {
                Debug.Log("[BuildWebGL] Switching platform to WebGL...");
                bool switched = EditorUserBuildSettings.SwitchActiveBuildTarget(
                    BuildTargetGroup.WebGL, BuildTarget.WebGL);
                if (!switched)
                {
                    Fail("切换 WebGL 平台失败（可能未安装 WebGL Build Support）");
                    return;
                }
            }

            // 收集场景：只构建 EditorBuildSettings 中已启用的场景
            string[] scenes = GetEnabledScenes();
            if (scenes.Length == 0)
            {
                Fail("没有可用场景（Build Settings 中为空或全部未勾选）");
                return;
            }
            Debug.Log("[BuildWebGL] Scenes: " + string.Join(", ", scenes));

            // Unity 会把 locationPathName 同时当作「输出文件夹」使用：
            // 若传 ".../index.html"，产物会落在 ".../index.html/" 子目录里。
            // 传目录本身，Unity 会在其中生成 index.html + Build/ + TemplateData/。
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = OutputDir,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            };

            Debug.Log("[BuildWebGL] Building to " + options.locationPathName);
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            Debug.Log(string.Format(
                "[BuildWebGL] Result={0} Errors={1} Warnings={2} Size={3}MB Duration={4}",
                summary.result, summary.totalErrors, summary.totalWarnings,
                summary.totalSize / 1024 / 1024, summary.totalTime));

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log("[BuildWebGL] BUILD SUCCEEDED");
                EditorApplication.Exit(0);
            }
            else
            {
                Debug.LogError("[BuildWebGL] BUILD FAILED");
                EditorApplication.Exit(1);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("[BuildWebGL] Exception: " + ex);
            EditorApplication.Exit(1);
        }
    }

    private static string[] GetEnabledScenes()
    {
        var list = new System.Collections.Generic.List<string>();
        foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
        {
            if (s.enabled && !string.IsNullOrEmpty(s.path))
                list.Add(s.path);
        }
        return list.ToArray();
    }

    private static void Fail(string msg)
    {
        Debug.LogError("[BuildWebGL] " + msg);
        EditorApplication.Exit(1);
    }
}
