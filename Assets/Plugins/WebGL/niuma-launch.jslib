// WebGL 下把浏览器的 location.search 原样交给 C#（避免 Application.absoluteURL 的编码差异），
// 并负责「关页即结算」：pagehide / beforeunload 时用 sendBeacon 回调平台退出接口。
mergeInto(LibraryManager.library, {
    NiuMa_GetLocationSearch: function () {
        var s = (typeof window !== "undefined" && window.location && window.location.search) ? window.location.search : "";
        var size = lengthBytesUTF8(s) + 1;
        var buf = _malloc(size);
        stringToUTF8(s, buf, size);
        return buf;
    },

    // exitToken / url 由 C# 传入（url 形如 http://host/api/v1/casino/niuma/exit-by-token）
    NiuMa_InstallExitBeacon: function (tokenPtr, urlPtr) {
        try {
            var token = UTF8ToString(tokenPtr);
            var url = UTF8ToString(urlPtr);
            if (!token || !url) return;
            var sent = false;
            function send() {
                if (sent) return;
                sent = true;
                var full = url + (url.indexOf("?") >= 0 ? "&" : "?") + "token=" + encodeURIComponent(token);
                try {
                    if (navigator.sendBeacon) {
                        navigator.sendBeacon(full, new Blob([], { type: "text/plain" }));
                    } else {
                        fetch(full, { method: "POST", keepalive: true });
                    }
                } catch (e) {
                    /* 关页时尽力而为，失败由平台兜底对账处理 */
                }
            }
            // 只监听「真的离开页面」的事件；切标签页不算退出，避免误结算
            window.addEventListener("pagehide", send);
            window.addEventListener("beforeunload", send);
            console.log("[Merchant] 退出结算 beacon 已注册");
        } catch (e) {
            console.error("[Merchant] 注册退出 beacon 失败", e);
        }
    },
});
