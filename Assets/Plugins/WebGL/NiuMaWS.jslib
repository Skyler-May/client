// NiuMaWS.jslib
//
// 浏览器原生 WebSocket 桥接
//
// 设计要点：
//  1. 用浏览器原生 WebSocket，而不是 Unity 的 ClientWebSocket——
//     自动复用浏览器 TLS/证书/代理策略，wss 无需客户端处理证书
//  2. binaryType='arraybuffer'，收发纯二进制，与 TCP 侧字节流完全一致
//     （服务端 WS 9098 与 TCP 10086 共用同一个 Session，帧格式都是裸 MessagePack）
//  3. 所有回调通过 SendMessage 投递回 Unity（Unity 主线程执行）
//  4. 句柄自增，支持多个连接（当前游戏只用一个，但保留扩展性）

mergeInto(LibraryManager.library, {

  // 内部状态：句柄 -> WebSocket 对象
  $NiuMaWSState: {
    nextHandle: 1,
    sockets: {}
  },

  NiuMaWS_Connect: function (urlPtr, goNamePtr) {
    var url = UTF8ToString(urlPtr);
    var goName = UTF8ToString(goNamePtr);
    var state = NiuMaWSState;

    var handle = state.nextHandle++;
    var ws;
    try {
      ws = new WebSocket(url);
    } catch (e) {
      // 构造失败（如 URL 非法、混合内容被拒）也要回告，不能静默
      SendMessage(goName, 'OnWebSocketError', 'WebSocket 构造失败: ' + e);
      return -1;
    }
    ws.binaryType = 'arraybuffer';
    ws.__niuMaHandle = handle;
    ws.__niuMaGoName = goName;
    state.sockets[handle] = ws;

    ws.onopen = function () {
      SendMessage(goName, 'OnWebSocketConnected', handle);
    };

    ws.onmessage = function (ev) {
      // 只处理二进制帧；文本帧说明服务端行为异常，记录下来便于排查
      if (typeof ev.data === 'string') {
        console.warn('[NiuMaWS] 收到文本帧，服务端应以二进制发送 MessagePack：', ev.data);
        return;
      }
      var bytes = new Uint8Array(ev.data);
      var len = bytes.length;
      var ptr = _malloc(len);
      HEAPU8.set(bytes, ptr);
      // 格式: handle|ptr|length，C# 侧解析后释放内存
      SendMessage(goName, 'OnWebSocketMessage', handle + '|' + ptr + '|' + len);
    };

    ws.onclose = function (ev) {
      var reason = (ev && ev.reason) ? ev.reason : ('code=' + (ev ? ev.code : '?'));
      delete state.sockets[handle];
      SendMessage(goName, 'OnWebSocketClosed', handle + '|' + reason);
    };

    ws.onerror = function (ev) {
      // onerror 之后浏览器必然触发 onclose，这里只记录，不重复回调
      console.error('[NiuMaWS] error handle=' + handle + ' url=' + url, ev);
      SendMessage(goName, 'OnWebSocketError', 'WebSocket error handle=' + handle);
    };

    return handle;
  },

  NiuMaWS_Send: function (handle, dataPtr, offset, count) {
    var ws = NiuMaWSState.sockets[handle];
    if (!ws) return 0;
    if (ws.readyState !== 1) {   // WebSocket.OPEN
      console.warn('[NiuMaWS] send 被跳过，连接状态=' + ws.readyState);
      return 0;
    }
    try {
      // 从 WASM 堆切出一份稳定副本（堆在下次 GC 后可能移动）
      var view = new Uint8Array(HEAPU8.buffer, dataPtr + offset, count);
      var copy = new Uint8Array(count);
      copy.set(view);
      ws.send(copy.buffer);
      return count;
    } catch (e) {
      console.error('[NiuMaWS] send 失败: ' + e);
      return 0;
    }
  },

  NiuMaWS_Close: function (handle) {
    var ws = NiuMaWSState.sockets[handle];
    if (!ws) return;
    try {
      ws.close(1000, 'client close');
    } catch (e) { }
    delete NiuMaWSState.sockets[handle];
  },

  // -------------------------------------------------------------------------
  // 聚合平台（Expo WebView）退出桥接
  //
  // NiuMa 原生的「返回」是 DestroyGameHall() → 回到 NiuMa 大厅。
  // 但在聚合平台里，根页面是 club 的棋牌列表，回 NiuMa 大厅是错的，
  // 玩家会看到一个不属于本平台的大厅，且无法再回 club。
  //
  // 因此这里把「返回」上抛给宿主：
  //   1) Expo WebView → window.ReactNativeWebView.postMessage（Expo 收到后 router.back()）
  //   2) 普通浏览器   → history.back()，若无历史则提示
  // -------------------------------------------------------------------------
  NiuMaWS_ExitToHost: function () {
    try {
      var msg = JSON.stringify({ type: 'niuma.exit' });

      if (window.ReactNativeWebView && window.ReactNativeWebView.postMessage) {
        window.ReactNativeWebView.postMessage(msg);
        return;
      }

      // 兼容直接用浏览器打开的场景
      if (window.parent && window.parent !== window) {
        window.parent.postMessage(msg, '*');
      }

      if (window.history && window.history.length > 1) {
        window.history.back();
      } else if (window.parent && window.parent !== window) {
        window.parent.postMessage(msg, '*');
      }
    } catch (e) {
      if (window.console && console.error) {
        console.error('[NiuMa] ExitToHost 失败: ' + e);
      }
    }
  },

  NiuMaWS_Free: function (ptr) {
    _free(ptr);
  }
});
