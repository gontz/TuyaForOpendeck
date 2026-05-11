/* Local minimal Stream Deck/OpenDeck property-inspector client shim.
 * Provides the subset used by this plugin:
 * - getSettings / setSettings
 * - getGlobalSettings / setGlobalSettings
 * - didReceiveSettings / didReceiveGlobalSettings subscription
 */
(function () {
  "use strict";

  function createEmitter() {
    var handlers = [];
    return {
      subscribe: function (fn) {
        if (typeof fn === "function") handlers.push(fn);
      },
      emit: function (payload) {
        handlers.slice().forEach(function (fn) {
          try { fn(payload); } catch (_) { }
        });
      }
    };
  }

  var query = new URLSearchParams(window.location.search || "");
  var wsPort = query.get("port");
  var registerEvent = query.get("registerEvent") || "registerPropertyInspector";
  var uuid = query.get("uuid") || query.get("context") || "";
  var action = query.get("action") || "";
  var context = query.get("context") || "";

  var didReceiveSettings = createEmitter();
  var didReceiveGlobalSettings = createEmitter();
  var ws = null;

  var pendingSettings = [];
  var pendingGlobal = [];
  var outboundQueue = [];

  function send(message) {
    if (!ws || ws.readyState !== WebSocket.OPEN) {
      outboundQueue.push(message);
      return;
    }
    ws.send(JSON.stringify(message));
  }

  function flushOutbound() {
    while (outboundQueue.length > 0 && ws && ws.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify(outboundQueue.shift()));
    }
  }

  function flushPendingResolvers(queue, value) {
    while (queue.length > 0) {
      var resolve = queue.shift();
      try { resolve(value); } catch (_) { }
    }
  }

  function connect() {
    if (!wsPort) return;
    try {
      ws = new WebSocket("ws://127.0.0.1:" + wsPort);
    } catch (_) {
      return;
    }

    ws.onopen = function () {
      send({ event: registerEvent, uuid: uuid });
      flushOutbound();
    };

    ws.onmessage = function (ev) {
      var data = null;
      try { data = JSON.parse(ev.data); } catch (_) { return; }
      if (!data || !data.event) return;

      if (data.event === "didReceiveSettings") {
        didReceiveSettings.emit(data);
        flushPendingResolvers(pendingSettings, data && data.payload ? data.payload.settings || {} : {});
        return;
      }

      if (data.event === "didReceiveGlobalSettings") {
        didReceiveGlobalSettings.emit(data);
        flushPendingResolvers(pendingGlobal, data && data.payload ? data.payload.settings || {} : {});
      }
    };
  }

  connect();

  window.SDPIComponents = {
    streamDeckClient: {
      didReceiveSettings: didReceiveSettings,
      didReceiveGlobalSettings: didReceiveGlobalSettings,

      getSettings: function () {
        return new Promise(function (resolve) {
          pendingSettings.push(resolve);
          send({ event: "getSettings", context: context, action: action });
        });
      },

      setSettings: function (settings) {
        send({ event: "setSettings", context: context, action: action, payload: settings || {} });
        return Promise.resolve();
      },

      getGlobalSettings: function () {
        return new Promise(function (resolve) {
          pendingGlobal.push(resolve);
          send({ event: "getGlobalSettings", context: context || uuid });
        });
      },

      setGlobalSettings: function (settings) {
        send({ event: "setGlobalSettings", context: context || uuid, payload: settings || {} });
        return Promise.resolve();
      }
    }
  };
})();
