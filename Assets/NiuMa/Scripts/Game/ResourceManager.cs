using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using UnityEngine;
using UnityEngine.Networking;
using XLua;

namespace NiuMa
{
    [LuaCallCSharp]
    public class ResourceManager
    {
        private ResourceManager() { }
        private static readonly ResourceManager _instance = new ResourceManager();

        public static ResourceManager Instance
        {
            get { return _instance; }
        }

        // WEB后端接口主机地址
        private string _httpHost = null;
        [BlackList]
        public string HttpHost
        {
            get { return _httpHost; }
            set { _httpHost = value; }
        }

        // AssetBundle文件服务器地址
        private string _assetBundleHost = null;
        [BlackList]
        public string AssetBundleHost
        {
            get { return _assetBundleHost; }
            set { _assetBundleHost = value; }
        }

#if UNITY_EDITOR
#elif (UNITY_STANDALONE || UNITY_ANDROID || UNITY_IOS || UNITY_WEBGL)
        // 远程AssetBundle哈希映射表
        private Dictionary<string, string> _remoteHashes = new Dictionary<string, string>();

        // 本地AssetBundle哈希映射表
        private Dictionary<string, string> _localHashes = new Dictionary<string, string>();

        // AssetBundle文件依赖表
        private Dictionary<string, HashSet<string>> _dependencies = new Dictionary<string, HashSet<string>>();

        // 当前已加载的AssetBundle文件
        private Dictionary<string, AssetBundle> _assetBundles = new Dictionary<string, AssetBundle>();

        // AssetBundle文件的最新访问时间，用于定时释放
        // 即：AssetBundle.Unload(false)
        private Dictionary<string, DateTime> _accessTimes = new Dictionary<string, DateTime>();

        // 间隔时间
        private float _interval = 0.0f;

        public static string GetLocalAssetBundlePath()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL 没有可写的本地文件系统（persistentDataPath 仅内存虚拟盘，
            // 且跨刷新不保留），AssetBundle 全部走 HTTP 下载 + 内存缓存。
            // 返回空串，调用方据此跳过所有本地文件操作。
            return string.Empty;
#else
            string localPath = Application.persistentDataPath + "/AssetBundle/";
            return localPath;
#endif
        }

        public Dictionary<string, string> GetRemoteHashes()
        {
            return _remoteHashes;
        }

        public Dictionary<string, string> GetLocalHashes()
        {
            return _localHashes;
        }

        // 判定指定的AssetBundle是否已经过时
        public bool IsAssetBundleOutOfDate(string ab)
        {
            string hash1 = "";
            string hash2 = "";
            if (!_remoteHashes.TryGetValue(ab, out hash1))
                return false;
            _localHashes.TryGetValue(ab, out hash2);
            if (hash1 != hash2)
                return true;
            return false;
        }

        // 更新本地哈希表
        public void UpdateLocalHash(string ab)
        {
            string hash = "";
            if (!_remoteHashes.TryGetValue(ab, out hash))
            {
                Debug.Log(string.Format("Update local hash failed, can't find asset bundle:\"{0}\"", ab));
                return;
            }
            _localHashes[ab] = hash;
        }

        // 从缓存中加载哈希表
        public static void LoadAssetHashes(byte[] buffer, ref Dictionary<string, string> assetHashes)
        {
            try
            {
                StreamReader sr = new StreamReader(new MemoryStream(buffer), System.Text.Encoding.UTF8);
                string line = null;
                string name = null;
                string hash = null;
                int pos = 0;
                while ((line = sr.ReadLine()) != null)
                {
                    pos = line.IndexOf('|');
                    if (pos == -1)
                        continue;
                    name = line.Substring(0, pos);
                    hash = line.Substring(pos + 1, (line.Length - (pos + 1)));
                    assetHashes.Add(name, hash);
                }
                sr.Close();
            }
            catch (Exception e)
            {
                Debug.LogError(e.Message);
            }
        }

        // 加载本地哈希表
        public void LoadLocalHashes()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL：无本地哈希文件，_localHashes 保持空 -> 所有 AB 视为需要下载
            return;
#endif
            string localPath = GetLocalAssetBundlePath();
            string file = localPath + "files.txt";
            if (File.Exists(file))
            {
                byte[] temp = File.ReadAllBytes(file);
                if (temp != null)
                    LoadAssetHashes(temp, ref _localHashes);
            }
        }

        // 保存本地哈希表
        public void SaveLocalHashes()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL：哈希只保存在内存字典中，不落盘（每次会话重新下载校验）
            return;
#endif
            string localPath = GetLocalAssetBundlePath();
            string file = localPath + "files.txt";
            if (File.Exists(file))
                File.Delete(file);
            FileStream fs = new FileStream(file, FileMode.CreateNew);
            if (fs != null)
            {
                StreamWriter sw = new StreamWriter(fs, System.Text.Encoding.Default);
                foreach (KeyValuePair<string, string> pr in _localHashes)
                    sw.WriteLine(pr.Key + "|" + pr.Value);
                sw.Close();
                fs.Close();
            }
        }

        // 处理远程哈希表
        public void OnDownloadRemoteHashes(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            _remoteHashes.Clear();
            _localHashes.Clear();
            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(text);
            LoadAssetHashes(buffer, ref _remoteHashes);
            LoadLocalHashes();

            // 删除不再使用的AssetBundle
            string path = null;
            string localPath = GetLocalAssetBundlePath();
            List<string> removedAssetBundles = new List<string>();
            foreach (KeyValuePair<string, string> pr in _localHashes)
            {
                if (!_remoteHashes.ContainsKey(pr.Key))
                {
                    path = localPath + pr.Key;
                    if (File.Exists(path))
                        File.Delete(path);
                    removedAssetBundles.Add(pr.Key);
                }
            }
            foreach (string key in removedAssetBundles)
                _localHashes.Remove(key);
        }

        public void OnDownloadDependencies(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;
            XmlDocument xml = new XmlDocument();
            xml.LoadXml(text);
            XmlElement root = xml.DocumentElement;
            if (root == null)
                return;
            _dependencies.Clear();
            HashSet<string> abList = null;
            XmlNodeList childNodes1 = root.ChildNodes;
            XmlNodeList childNodes2 = null;
            XmlElement elt = null;
            for (int i = 0; i < childNodes1.Count; i++)
            {
                elt = childNodes1[i] as XmlElement;
                if (elt == null)
                    continue;
                if (!elt.Name.Equals("AssetBundle"))
                    continue;
                string name = elt.GetAttribute("Name");
                if (_dependencies.ContainsKey(name))
                    abList = _dependencies[name];
                else
                {
                    abList = new HashSet<string>();
                    _dependencies[name] = abList;
                }
                //Debug.LogFormat("Asset bundle \"{0}\" dependencies:", name);
                childNodes2 = childNodes1[i].ChildNodes;
                if (childNodes2 == null)
                    continue;
                for (int j = 0; j < childNodes2.Count; j++)
                {
                    elt = childNodes2[j] as XmlElement;
                    if (elt == null)
                        continue;
                    if (!elt.Name.Equals("Dependency"))
                        continue;
                    abList.Add(elt.InnerText);
                    //Debug.Log(elt.InnerText);
                }
            }
        }

        private AssetBundle GetAssetBundle(string name)
        {
            HashSet<string> traversedNames = new HashSet<string>();
            AssetBundle ab = GetAssetBundleRecursive(name, traversedNames);
            return ab;
        }

        /// <summary>
        /// 递归加载AssetBundle文件的全部依赖AssetBundle文件
        /// </summary>
        /// <param name="name">AssetBundle文件名</param>
        /// <param name="traversedNames">已遍历的AssetBundle文件名列表，避免循环递归</param>
        /// <returns></returns>
        private AssetBundle GetAssetBundleRecursive(string name, HashSet<string> traversedNames)
        {
            AssetBundle ab = null;
            if (_assetBundles.ContainsKey(name))
                ab = _assetBundles[name];
            else
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                // WebGL：不读本地文件。AB 必须已被 DownloadAssetBundlesFromWebGL
                // 预先下载进 _assetBundles。走到这里说明调用方没提前加载依赖。
                Debug.LogErrorFormat(
                    "[WebGL] AssetBundle \"{0}\" 未预加载。请先调用 " +
                    "ResourceManager.DownloadAssetBundlesFromWebGL 下载该游戏的依赖包。",
                    name);
                ab = null;
#else
                string path = GetLocalAssetBundlePath();
                path = path + name;
                if (File.Exists(path))
                    ab = AssetBundle.LoadFromFile(path);
                if (ab != null)
                {
                    _assetBundles[name] = ab;
                    Debug.LogFormat("Load asset bundle \"{0}\" success.", name);
                }
                else
                    Debug.LogErrorFormat("Load asset bundle \"{0}\" failed.", name);
#endif
            }
            if (ab != null)
            {
                _accessTimes[name] = DateTime.Now;
                traversedNames.Add(name);
                HashSet<string> dependencies = null;
                if (_dependencies.ContainsKey(name))
                    dependencies = _dependencies[name];
                if (dependencies == null || dependencies.Count == 0)
                    return ab;
                AssetBundle tmp = null;
                foreach (string dep in dependencies)
                {
                    if (traversedNames.Contains(dep))
                        continue;
                    tmp = GetAssetBundleRecursive(dep, traversedNames);
                    if (tmp == null)
                    {
                        ab = null;
                        break;
                    }
                }
            }
            return ab;
        }

        public bool HasAssetBundle(string name)
        {
            return _assetBundles.ContainsKey(name);
        }
#endif

        public UnityEngine.Object LoadResource(string resName, string assetBundle = "", string path = "", string ext = "")
        {
            UnityEngine.Object obj = Resources.Load(resName);
            if (obj == null)
            {
#if UNITY_EDITOR
                // 编辑器仅支持Resources.Load
#elif (UNITY_STANDALONE || UNITY_ANDROID || UNITY_IOS || UNITY_WEBGL)
                // Android及IOS支持AssetBundle
                string name = path;
                if (name.Length > 0)
                {
                    name = name.Replace('\\', '/');
                    if (name[name.Length - 1] != '/')
                        name += "/";
                }
                name = name + resName + ext;
                AssetBundle ab = GetAssetBundle(assetBundle);
                if (ab != null)
                {
                    obj = ab.LoadAsset(name);
                    if (obj == null)
                        Debug.LogErrorFormat("Asset bundle \"{0}\" dosn't contain asset \"{1}\"", assetBundle, name);
                }
#endif
            }
            return obj;
        }

#if UNITY_EDITOR
#elif (UNITY_STANDALONE || UNITY_ANDROID || UNITY_IOS || UNITY_WEBGL)
        public void Update()
        {
            _interval += Time.unscaledDeltaTime;
            if (_interval < 5.0f)
                return;
            // 梅5秒检查一次
            _interval = 0.0f;
            DateTime nowTime = DateTime.Now;
            List<string> removeNames = new List<string>();
            foreach (KeyValuePair<string, DateTime> pr in _accessTimes)
            {
                // LUA脚本永不释放
                if (pr.Key.Equals("luascripts.ab"))
                    continue;
                if (!_assetBundles.ContainsKey(pr.Key))
                {
                    removeNames.Add(pr.Key);
                    continue;
                }
                TimeSpan ts = nowTime - pr.Value;
                if (ts.TotalSeconds < 30.0)
                    continue;
                // 30秒未被使用，释放
                AssetBundle ab = _assetBundles[pr.Key];
                ab.Unload(false);
                removeNames.Add(pr.Key);
            }
            foreach (string name in removeNames)
            {
                _assetBundles.Remove(name);
                _accessTimes.Remove(name);
            }
        }
#endif

#if UNITY_WEBGL && !UNITY_EDITOR
        // ==================================================================
        // WebGL 资源加载：按需下载 AssetBundle 到内存
        //
        // 设计要点（为「一个厂商几十款游戏」的可扩展性）：
        //   1. 按需加载：点某款游戏时才下载该游戏的 AB，而不是全量预载。
        //      本地 3 款时全量才 64MB，几十款时会到 GB 级，必须按需。
        //   2. 依赖驱动：AB 列表来自 Dependencies.xml，
        //      新增游戏只要重新打包生成清单，本代码无需改动。
        //   3. 内存缓存：下载过的 AB 存在 _assetBundles，
        //      之后 LoadResource 走原有同步路径，调用方零改动。
        //   4. 并发下载：同一批 AB 并行请求，缩短等待。
        // ==================================================================

        /// <summary>
        /// 下载并加载指定的一批 AssetBundle 到内存（协程）
        /// </summary>
        /// <param name="abNames">AssetBundle 名称列表，如 prefabs/biji.ab</param>
        /// <param name="onProgress">进度回调（0~1）</param>
        /// <param name="onDone">完成回调，参数为是否全部成功</param>
        public IEnumerator DownloadAssetBundlesFromWebGL(
            List<string> abNames,
            Action<float> onProgress,
            Action<bool> onDone)
        {
            if (abNames == null || abNames.Count == 0)
            {
                if (onDone != null) onDone(true);
                yield break;
            }

            // 过滤掉已经加载过的
            List<string> pending = new List<string>();
            foreach (string name in abNames)
            {
                if (string.IsNullOrEmpty(name))
                    continue;
                if (_assetBundles.ContainsKey(name))
                    continue;
                pending.Add(name);
            }

            if (pending.Count == 0)
            {
                Debug.Log("[WebGL] 所有 AssetBundle 已缓存，无需下载");
                if (onProgress != null) onProgress(1.0f);
                if (onDone != null) onDone(true);
                yield break;
            }

            Debug.LogFormat("[WebGL] 需下载 {0} 个 AssetBundle", pending.Count);

            int done = 0;
            int failed = 0;
            float totalBytes = 0f;

            // 逐个下载（UnityWebRequest 自身并发能力有限，串行更稳定；
            // 若后续要提速，可改为分批并发）
            foreach (string name in pending)
            {
                string url = AssetBundleHost + name;
                UnityWebRequest req = UnityWebRequestAssetBundle.GetAssetBundle(url);
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogErrorFormat("[WebGL] 下载 AssetBundle \"{0}\" 失败: {1}", name, req.error);
                    failed++;
                }
                else
                {
                    AssetBundle ab = DownloadHandlerAssetBundle.GetContent(req);
                    if (ab == null)
                    {
                        Debug.LogErrorFormat("[WebGL] AssetBundle \"{0}\" 下载成功但解析失败", name);
                        failed++;
                    }
                    else
                    {
                        _assetBundles[name] = ab;
                        _accessTimes[name] = DateTime.Now;
                        Debug.LogFormat("[WebGL] AssetBundle \"{0}\" 加载成功", name);
                    }
                }

                done++;
                totalBytes += req.downloadedBytes;
                if (onProgress != null)
                    onProgress((float)done / pending.Count);

                req.Dispose();
            }

            Debug.LogFormat("[WebGL] 加载完成: 成功={0} 失败={1} 总流量={2:F1}KB",
                pending.Count - failed, failed, totalBytes / 1024f);

            if (onDone != null)
                onDone(failed == 0);
        }

        /// <summary>
        /// 确保基础包已就绪（common / 大厅 / 游戏列表）
        /// 这些是进入任何界面前都需要的，启动时统一加载
        /// </summary>
        public static List<string> GetBaseAssetBundles()
        {
            return new List<string>
            {
                "common.ab",
                "gamelist.ab",
                "luascripts.ab",
                "hall/main.ab",
                "hall/dialogs.ab",
                "images/main.ab",
                "images/chat.ab",
                "images/emojis.ab",
                "prefabs/common.ab",
                "prefabs/hall.ab",
                "prefabs/gamelist.ab",
                "sounds/main.ab",
            };
        }
#endif
    }
}