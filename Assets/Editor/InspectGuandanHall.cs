using System.Reflection;
using UnityEditor;
using UnityEngine;
using System.Text;

namespace NiuMa.Editor
{
    /// <summary>诊断：查看掼蛋大厅预制体上挂了什么脚本/LuaBehaviour 的脚本名</summary>
    public static class InspectGuandanHall
    {
        [MenuItem("NiuMa/Inspect GuanDan Hall")]
        public static void Inspect()
        {
            string path = "Assets/NiuMa/Resources/Prefabs/Game/GuanDan/Hall.prefab";
            GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null)
            {
                Debug.LogError("[Inspect] 找不到 " + path);
                return;
            }
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[Inspect] 预制体: " + path + " active=" + go.activeSelf);
            MonoBehaviour[] comps = go.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (MonoBehaviour c in comps)
            {
                if (c == null) continue;
                sb.Append("  - ").Append(c.GetType().Name);
                // 打印 LuaBehaviour 的脚本字段
                FieldInfo[] fields = c.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (FieldInfo f in fields)
                {
                    if (f.Name.ToLower().Contains("script") || f.Name.ToLower().Contains("lua"))
                    {
                        object v = f.GetValue(c);
                        sb.Append(" [").Append(f.Name).Append("=").Append(v == null ? "null" : v.ToString()).Append("]");
                    }
                }
                sb.AppendLine();
            }
            // 对照：麻将大厅
            GameObject mj = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/NiuMa/Resources/Prefabs/Game/Mahjong/Hall.prefab");
            sb.AppendLine("[Inspect] 对照 麻将大厅: " + (mj == null ? "缺失" : "存在"));
            if (mj != null)
            {
                foreach (MonoBehaviour c in mj.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (c == null) continue;
                    sb.Append("  - ").Append(c.GetType().Name);
                    FieldInfo[] fields = c.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    foreach (FieldInfo f in fields)
                    {
                        if (f.Name.ToLower().Contains("script") || f.Name.ToLower().Contains("lua"))
                        {
                            object v = f.GetValue(c);
                            sb.Append(" [").Append(f.Name).Append("=").Append(v == null ? "null" : v.ToString()).Append("]");
                        }
                    }
                    sb.AppendLine();
                }
            }
            Debug.Log(sb.ToString());
        }
    }
}
