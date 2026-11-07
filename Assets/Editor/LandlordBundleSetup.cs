using UnityEditor;
using UnityEngine;

namespace NiuMa.EditorTools
{
    /// <summary>
    /// 为斗地主资源设置 AssetBundle 名称（prefabs/landlord.ab）
    /// 与现有游戏保持一致（如 prefabs/lackey.ab）
    /// </summary>
    public static class LandlordBundleSetup
    {
        private const string BundleName = "prefabs/landlord.ab";

        [MenuItem("NiuMa/Landlord/Setup AssetBundle")]
        public static void Setup()
        {
            string[] prefabs =
            {
                "Assets/NiuMa/Resources/Prefabs/Game/Landlord/Hall.prefab",
                "Assets/NiuMa/Resources/Prefabs/Game/Landlord/Room.prefab",
                "Assets/NiuMa/Resources/Prefabs/Game/Landlord/RoomItem.prefab"
            };
            foreach (string p in prefabs)
                Assign(p);
            Assign("Assets/NiuMa/Game/Landlord/ui_white.png");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[LandlordBundleSetup] bundle name assigned: " + BundleName);
        }

        private static void Assign(string path)
        {
            var importer = AssetImporter.GetAtPath(path);
            if (importer == null)
            {
                Debug.LogWarning("[LandlordBundleSetup] missing asset: " + path);
                return;
            }
            if (importer.assetBundleName == BundleName)
                return;
            importer.assetBundleName = BundleName;
            importer.SaveAndReimport();
            Debug.Log("[LandlordBundleSetup] " + path + " -> " + BundleName);
        }
    }
}
