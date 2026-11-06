// 浏览器 WebSocket 桥接：给 Unity WebGL 当「TCP 替代品」
// 协议与 TCP 一致：二进制 msgpack 帧，不加帧头。
//
// 重要：Unity 的 emscripten 构建会丢弃 jslib 顶层声明的变量/函数（只在 mergeInto 里的函数保留），
// 所以：① 状态一律挂 globalThis；② 逻辑全部内联在库函数里，不定义顶层 helper。
mergeInto(LibraryManager.library, {
    NiuMa_WsConnect: function (urlPtr) {
        var G = typeof globalThis !== "undefined" ? globalThis : window;
        try {
            var url = UTF8ToString(urlPtr);
            var S = G.__niuMaWs;
            if (!S) {
                S = { ws: null, state: 0, queue: [] };
                G.__niuMaWs = S;
            }
            if (S.ws) {
                try { S.ws.close(); } catch (e) { /* ignore */ }
            }
            S.queue = [];
            S.state = 0;
            var ws = new WebSocket(url);
            ws.binaryType = "arraybuffer";
            S.ws = ws;
            ws.onopen = function () {
                S.state = 1;
                console.log("[Net] WS open " + url);
            };
            ws.onmessage = function (ev) {
                try {
                    if (typeof ev.data === "string") return;
                    var u8 = new Uint8Array(ev.data);
                    var CHUNK = 0x8000;
                    var parts = [];
                    for (var i = 0; i < u8.length; i += CHUNK) {
                        parts.push(String.fromCharCode.apply(null, u8.subarray(i, i + CHUNK)));
                    }
                    S.queue.push(btoa(parts.join("")));
                } catch (e) {
                    console.error("[Net] WS message error", e);
                }
            };
            ws.onclose = function (ev) {
                S.state = 2;
                console.log("[Net] WS closed code=" + ev.code + " reason=" + ev.reason);
            };
            ws.onerror = function (e) {
                S.state = 3;
                console.error("[Net] WS error", e);
            };
        } catch (e) {
            if (G.__niuMaWs) G.__niuMaWs.state = 3;
            console.error("[Net] WS connect exception", e);
        }
    },

    NiuMa_WsState: function () {
        var G = typeof globalThis !== "undefined" ? globalThis : window;
        return G.__niuMaWs ? G.__niuMaWs.state : 3;
    },

    NiuMa_WsSend: function (b64Ptr) {
        var G = typeof globalThis !== "undefined" ? globalThis : window;
        try {
            var S = G.__niuMaWs;
            if (!S || !S.ws || S.state !== 1) return;
            var b64 = UTF8ToString(b64Ptr);
            var bin = atob(b64);
            var len = bin.length;
            var u8 = new Uint8Array(len);
            for (var i = 0; i < len; i++) u8[i] = bin.charCodeAt(i);
            S.ws.send(u8.buffer);
        } catch (e) {
            console.error("[Net] WS send error", e);
        }
    },

    NiuMa_WsPoll: function () {
        var G = typeof globalThis !== "undefined" ? globalThis : window;
        var S = G.__niuMaWs;
        if (!S || !S.queue.length) return 0;
        var b64 = S.queue.shift();
        var size = lengthBytesUTF8(b64) + 1;
        var buf = _malloc(size);
        stringToUTF8(b64, buf, size);
        return buf;
    },

    NiuMa_WsClose: function () {
        var G = typeof globalThis !== "undefined" ? globalThis : window;
        try {
            var S = G.__niuMaWs;
            if (!S) return;
            S.state = 2;
            if (S.ws) {
                S.ws.close();
                S.ws = null;
            }
        } catch (e) { /* ignore */ }
    },
});
