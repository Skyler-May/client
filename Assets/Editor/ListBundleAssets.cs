using System.Text;
using UnityEditor;
using UnityEngine;

namespace NiuMa.Editor
{
    /// <summary>列出指定 AssetBundle 里的资产名，用于核对预制体路径</summary>
    public static class ListBundleAssets
    {
        [MenuItem("NiuMa/List Bundle Assets")]
        public static void List()
        {
            string[] bundles = { "AssetBundle/webgl/prefabs/guandan.ab", "AssetBundle/webgl/prefabs/mahjong_main.ab" };
            foreach (string path in bundles)
            {
                AssetBundle bundle = AssetBundle.LoadFromFile(path);
                StringBuilder sb = new StringBuilder();
                sb.Append("[ListBundle] ").Append(path);
                if (bundle == null)
                {
                    sb.Append(" -> 打不开");
                    Debug.Log(sb.ToString());
                    continue;
                }
                string[] names = bundle.GetAllAssetNames();
                sb.Append(" assets=").Append(names.Length);
                foreach (string n in names)
                    sb.Append("\n   ").Append(n);
                Debug.Log(sb.ToString());
                bundle.Unload(true);
            }
        }
    }
}
