using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using NiuMa;

public class GameEntry : MonoBehaviour
{
    public string _gameName = "";
    public string _gameAlias = "";
    private GameObject _new = null;
    private GameObject _download = null;
    private Slider _progress = null;
    private Text _precent = null;

#if UNITY_EDITOR
#elif UNITY_WEBGL
    private bool _downloading = false;   // 是否正在下载 AssetBundle
    private bool _webglLoaded = false;   // 本游戏的依赖是否已下载完成
#elif (UNITY_STANDALONE || UNITY_ANDROID || UNITY_IOS)
    private List<string> _assetBundles = new List<string>();
    private int _current = 0;
    private bool _outdated = false;     // 是否需要更新
    private bool _downloading = false;  // 是否正在下载AssetBundle
#endif

    private void Awake()
    {
        Transform child = gameObject.transform.Find("Button/New");
        if (child != null)
            _new = child.gameObject;
        child = gameObject.transform.Find("Button/Download");
        if (child != null)
            _download = child.gameObject;
        child = gameObject.transform.Find("Button/Progress");
        if (child != null)
        {
            _progress = child.gameObject.GetComponent<Slider>();
            child = child.Find("Text");
            if (child != null)
                _precent = child.gameObject.GetComponent<Text>();
        }
    }

    // Use this for initialization
    private void Start()
    {
#if UNITY_EDITOR
#elif UNITY_WEBGL
        // WebGL：无本地缓存，无需"是否过时"判断，点击时按需下载依赖
#elif (UNITY_STANDALONE || UNITY_ANDROID || UNITY_IOS)
        _outdated = GameManifest.Instance.IsGameOutdated(_gameName);
        if (_outdated)
        {
            if (_new != null)
                _new.SetActive(true);
            if (_download != null)
                _download.SetActive(true);
        }
        else
            GameManager.Instance.AddGameLoaded(_gameName);
#endif
    }

    // Update is called once per frame
    private void Update()
    {}

    public void OnClick()
    {
#if UNITY_EDITOR
        EnterGame();
#elif UNITY_WEBGL
        // WebGL：按需下载该游戏依赖的 AssetBundle 后再进入
        //
        // 为什么要按需：一个厂商会有几十款游戏（含后续从网狐移植的），
        // 全量预加载会到 GB 级，启动必然超时。这里只下载当前游戏需要的包，
        // 依赖列表来自打包时生成的 Dependencies.xml，新增游戏无需改代码。
        if (_downloading)
            return;
        if (_webglLoaded)
        {
            EnterGame();
            return;
        }
        if (_progress != null)
            _progress.gameObject.SetActive(true);
        StartCoroutine(DownloadForWebGL());
#elif (UNITY_STANDALONE || UNITY_ANDROID || UNITY_IOS)
        if (_downloading)
            return;
        if (_outdated)
        {
            _assetBundles.Clear();
            _current = 0;
            GameManifest.Instance.GetGameOutdatedABs(_gameName, ref _assetBundles);
            if (_assetBundles.Count > 0)
            {
                _downloading = true;

                if (_progress != null)
                    _progress.gameObject.SetActive(true);
                DownloadNextAssetBundle();
            }
            else
                SetUpdated();
        }
        else
            EnterGame();
#endif
    }

    /// <summary>
    /// 聚合平台直达：不解压、不判断更新，直接进指定游戏
    ///
    /// 由 GameManager 在大厅加载完成后调用（见 TryAutoEnterLaunchGame），
    /// 保证「Expo 点游戏 → 免登录 → 直达该游戏」，不经过大厅点击。
    /// </summary>
    public void EnterDirectly()
    {
        EnterGame();
    }

#if UNITY_EDITOR
#elif (UNITY_STANDALONE || UNITY_ANDROID || UNITY_IOS)
    private void SetUpdated()
    {
        GameManifest.Instance.SetGameUpdated(_gameName);
        _outdated = false;
        if (_new != null)
            _new.SetActive(false);
        if (_download != null)
            _download.SetActive(false);
    }

    private void DownloadNextAssetBundle()
    {
        if (_current < _assetBundles.Count)
        {
            float ratio = (float)_current / (float)_assetBundles.Count;
            if (_progress != null)
                _progress.value = ratio;
            ratio *= 100.0f;
            if (_precent != null)
                _precent.text = string.Format("{0}%", (int)ratio);

            DownloadAssetBundle();
        }
        else
        {
            _downloading = false;
            if (_progress != null)
                _progress.gameObject.SetActive(false);

            SetUpdated();

            // 下载完成，更新本地哈希并保存哈希文件
            foreach (string ab in _assetBundles)
                ResourceManager.Instance.UpdateLocalHash(ab);
            ResourceManager.Instance.SaveLocalHashes();
            string tip = "游戏【" + _gameAlias + "】更新完成！";
            GameManager.Instance.ShowPromptTip(tip, 3.0f);
            GameManager.Instance.AddGameLoaded(_gameName);
        }
    }

    private void DownloadAssetBundle()
    {
        if (_current < _assetBundles.Count && _current >= 0)
        {
            string name = _assetBundles[_current];
            string url = ResourceManager.Instance.AssetBundleHost + name;
            string path = ResourceManager.GetLocalAssetBundlePath();
            path = path + name;
            HttpRequester.Instance.Download(url, path, OnAssetBundleDownloaded);
        }
    }

    private void OnAssetBundleDownloaded(int code, string path)
    {
        if (code == 200)
        {
            _current++;
            DownloadNextAssetBundle();
        }
        else
        {
            Debug.LogErrorFormat("Update game \"{0}\" failed, download asset bundle \"{1}\" error: {2}", _gameName, _assetBundles[_current], path);
            GameManager.Instance.ShowPromptDialog("更新游戏失败，请检查网络状态。");
        }
    }
#endif

#if UNITY_WEBGL && !UNITY_EDITOR
    /// <summary>
    /// 按需下载当前游戏的 AssetBundle 后进入游戏
    ///
    /// 依赖来源：GameManifest.GetGameAssetBundles（由打包生成的 Dependencies.xml 解析）
    /// 若清单里查不到该游戏，回落到命名约定 prefabs/{gameName小写}.ab
    /// —— 这对新增游戏友好：打包脚本按目录名生成 AB，代码无需登记。
    /// </summary>
    private System.Collections.IEnumerator DownloadForWebGL()
    {
        _downloading = true;

        // 1) 收集该游戏需要的 AB
        List<string> abs = new List<string>();
        GameManifest.Instance.GetGameAssetBundles(_gameName, ref abs);

        if (abs.Count == 0)
        {
            // 清单未收录：按目录命名约定回退
            string guess = "prefabs/" + _gameName.ToLower() + ".ab";
            abs.Add(guess);
            Debug.LogWarningFormat(
                "[WebGL] 清单中未找到游戏 \"{0}\" 的 AssetBundle，回退使用 {1}",
                _gameName, guess);
        }

        // 2) 叠加基础包（大厅/公共资源，通常已在启动时加载，这里保证不遗漏）
        foreach (string baseAb in ResourceManager.GetBaseAssetBundles())
        {
            if (!abs.Contains(baseAb))
                abs.Add(baseAb);
        }

        Debug.LogFormat("[WebGL] 游戏 \"{0}\" 需加载 {1} 个 AssetBundle", _gameName, abs.Count);

        // 3) 下载
        bool ok = false;
        yield return ResourceManager.Instance.DownloadAssetBundlesFromWebGL(
            abs,
            (progress) =>
            {
                if (_progress != null)
                    _progress.value = progress;
                if (_precent != null)
                    _precent.text = Mathf.RoundToInt(progress * 100f) + "%";
            },
            (success) => { ok = success; });

        _downloading = false;

        if (ok)
        {
            _webglLoaded = true;
            GameManager.Instance.AddGameLoaded(_gameName);
            if (_progress != null)
                _progress.gameObject.SetActive(false);
            EnterGame();
        }
        else
        {
            Debug.LogErrorFormat("[WebGL] 游戏 \"{0}\" 资源加载失败", _gameName);
            if (_progress != null)
                _progress.gameObject.SetActive(false);
            GameManager.Instance.ShowPromptTip("游戏资源加载失败，请检查网络后重试", 3.0f);
        }
    }
#endif

    private void EnterGame()
    {
        Hall hall = GameManager.Instance.Hall;
        if (hall != null)
            hall.OnGameClick(_gameName);
    }
}