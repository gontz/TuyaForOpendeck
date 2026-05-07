(function () {
  'use strict';

  var _ready = false;
  var _bindings = [];
  var _suppressIncoming = 0;
  var _activeSlider = null;

  function getNested(obj, path) {
    var parts = path.split('.');
    var cur = obj;
    for (var i = 0; i < parts.length; i++) {
      if (cur == null) return undefined;
      cur = cur[parts[i]];
    }
    return cur;
  }

  function setNested(obj, path, value) {
    var parts = path.split('.');
    var cur = obj;
    for (var i = 0; i < parts.length - 1; i++) {
      if (cur[parts[i]] == null || typeof cur[parts[i]] !== 'object') {
        cur[parts[i]] = {};
      }
      cur = cur[parts[i]];
    }
    cur[parts[parts.length - 1]] = value;
  }

  function unwrap(payload) {
    if (!payload) return {};
    if (payload.settings && typeof payload.settings === 'object') return payload.settings;
    if (payload.payload && payload.payload.settings && typeof payload.payload.settings === 'object')
      return payload.payload.settings;
    return payload;
  }

  function readValue(el) {
    if (el.type === 'checkbox') return el.checked;
    if (el.type === 'range' || el.type === 'number') return Number(el.value);
    return el.value;
  }

  function writeValue(el, val) {
    if (el.type === 'checkbox') {
      el.checked = !!val;
    } else {
      el.value = val != null ? val : '';
    }
  }

  function bindElement(el, settingPath, opts) {
    opts = opts || {};
    var binding = { el: el, path: settingPath, opts: opts };
    _bindings.push(binding);

    var valueDisplay = null;
    if (el.type === 'range' && opts.valueDisplay) {
      valueDisplay = document.getElementById(opts.valueDisplay);
    }

    function onUserChange() {
      var val = readValue(el);
      if (valueDisplay) valueDisplay.textContent = val;
      if (opts.onChange) opts.onChange(val);

      _suppressIncoming = Date.now() + 1200;
      SDPIComponents.streamDeckClient.getSettings().then(function (s) {
        var cur = unwrap(s);
        setNested(cur, settingPath, val);
        SDPIComponents.streamDeckClient.setSettings(cur);
      });
    }

    if (el.type === 'range') {
      el.addEventListener('mousedown', function () { _activeSlider = el; });
      el.addEventListener('touchstart', function () { _activeSlider = el; });
      window.addEventListener('mouseup', function () {
        if (_activeSlider === el) _activeSlider = null;
      });
      window.addEventListener('touchend', function () {
        if (_activeSlider === el) _activeSlider = null;
      });
      el.addEventListener('input', function () {
        if (valueDisplay) valueDisplay.textContent = el.value;
      });
      el.addEventListener('change', onUserChange);
    } else if (el.tagName === 'SELECT') {
      el.addEventListener('change', onUserChange);
    } else if (el.type === 'checkbox') {
      el.addEventListener('change', onUserChange);
    } else {
      el.addEventListener('change', onUserChange);
      el.addEventListener('blur', onUserChange);
    }

    return binding;
  }

  function populateAll(settings) {
    if (Date.now() < _suppressIncoming) return;
    for (var i = 0; i < _bindings.length; i++) {
      var b = _bindings[i];
      if (b.el === _activeSlider) continue;
      var val = getNested(settings, b.path);
      var defaultVal = b.opts.defaultValue;
      if (val == null && defaultVal != null) val = defaultVal;
      writeValue(b.el, val);
      if (b.el.type === 'range' && b.opts.valueDisplay) {
        var disp = document.getElementById(b.opts.valueDisplay);
        if (disp) disp.textContent = val != null ? val : b.el.value;
      }
    }
  }

  window.TuyaSettings = {
    unwrap: unwrap,
    getNested: getNested,
    setNested: setNested,

    bind: function (elementOrId, settingPath, opts) {
      var el = typeof elementOrId === 'string'
        ? document.getElementById(elementOrId) : elementOrId;
      if (!el) return;
      return bindElement(el, settingPath, opts);
    },

    init: function (callback) {
      if (_ready) return;
      _ready = true;

      SDPIComponents.streamDeckClient.getSettings().then(function (s) {
        var settings = unwrap(s);
        populateAll(settings);
        if (callback) callback(settings);
      });

      SDPIComponents.streamDeckClient.didReceiveSettings.subscribe(function (s) {
        var settings = unwrap(s);
        populateAll(settings);
      });
    },

    getSettings: function () {
      return SDPIComponents.streamDeckClient.getSettings().then(unwrap);
    },

    saveSettings: function (settings) {
      _suppressIncoming = Date.now() + 1200;
      return SDPIComponents.streamDeckClient.setSettings(settings);
    },

    getGlobalSettings: function () {
      return SDPIComponents.streamDeckClient.getGlobalSettings().then(unwrap);
    },

    onGlobalSettingsChanged: function (fn) {
      if (SDPIComponents.streamDeckClient.didReceiveGlobalSettings) {
        SDPIComponents.streamDeckClient.didReceiveGlobalSettings.subscribe(fn);
      }
    }
  };
})();
