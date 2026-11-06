// 页面内调试日志浮层（仅 ?debug=1 时启用）：把 console / 未捕获异常显示在角落里，
// 方便在 WebView 里直接看到 Unity 端的日志（Android WebView 不方便开远程调试）。
mergeInto(LibraryManager.library, {
    NiuMa_InstallDebugOverlay: function () {
        var G = typeof globalThis !== "undefined" ? globalThis : window;
        try {
            if (G.__niuMaDebugInstalled) return;
            G.__niuMaDebugInstalled = true;

            var box = document.createElement("div");
            box.id = "gc-debug";
            box.style.cssText =
                "position:fixed;left:0;top:0;max-width:52%;max-height:70%;overflow:auto;z-index:999999;" +
                "background:rgba(0,0,0,0.72);color:#7CFC98;font:11px/1.35 monospace;padding:6px;" +
                "white-space:pre-wrap;pointer-events:none;border-radius:0 0 6px 0;";
            document.body.appendChild(box);

            var lines = [];
            function push(tag, msg) {
                lines.push(tag + " " + msg);
                if (lines.length > 120) lines.shift();
                box.textContent = lines.join("\n");
                box.scrollTop = box.scrollHeight;
            }

            var origLog = console.log;
            var origWarn = console.warn;
            var origErr = console.error;
            console.log = function () {
                try { push("LOG", Array.prototype.map.call(arguments, String).join(" ")); } catch (e) { }
                origLog.apply(console, arguments);
            };
            console.warn = function () {
                try { push("WARN", Array.prototype.map.call(arguments, String).join(" ")); } catch (e) { }
                origWarn.apply(console, arguments);
            };
            console.error = function () {
                try { push("ERR", Array.prototype.map.call(arguments, String).join(" ")); } catch (e) { }
                origErr.apply(console, arguments);
            };
            window.onerror = function (m, src, line, col) {
                push("JS-ERR", m + " @" + (src || "") + ":" + line + ":" + col);
            };
            window.addEventListener("unhandledrejection", function (e) {
                push("PROMISE-ERR", String(e.reason || e));
            });

            push("SYS", "debug overlay ready, url=" + location.href.slice(0, 80));
        } catch (e) {
            /* ignore */
        }
    },
});
