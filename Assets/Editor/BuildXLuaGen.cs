using UnityEditor;
using UnityEngine;

/// <summary>
/// batchmode 入口：执行 xLua 完整代码生成。
///
/// 必须调用 GenAll()（= 菜单「XLua/Generate Code」），
/// 因为它内部会先 GetGenConfig() 初始化 LuaCallCSharp / CSharpCallLua 等列表，
/// 再依次生成 delegate bridge、enum wrap、class wrap、lua register。
///
/// 直接调 GenCodeForClass() 会因列表为 null 抛 ArgumentNullException。
///
/// 为什么需要这步：WebGL 下 xLua 的 CSharpCallLua 接口（NiuMa.ILuaBehaviour）
/// 必须由生成代码提供桥接，否则运行时
/// InvalidCastException: This type must add to CSharpCallLua。
/// </summary>
public class BuildXLuaGen
{
    public static void Run()
    {
        Debug.Log("[XLuaGen] ===== start =====");
        CSObjectWrapEditor.Generator.GenAll();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[XLuaGen] DONE");
    }
}
