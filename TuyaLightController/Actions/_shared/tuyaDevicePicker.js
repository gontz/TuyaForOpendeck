(function () {
  'use strict';

  var CACHE_KEY = '__tuyaDevicePickerCache';
  var CACHE_TTL_MS = 30 * 1000;
  var STORAGE_KEY = 'tuyaPluginGlobalSettings';

  function unwrap(payload) {
    if (!payload) return {};
    if (payload.settings && typeof payload.settings === 'object') return payload.settings;
    if (payload.payload && payload.payload.settings && typeof payload.payload.settings === 'object')
      return payload.payload.settings;
    return payload;
  }

  function readStoredSettings() {
    try {
      var raw = window.localStorage.getItem(STORAGE_KEY);
      return raw ? JSON.parse(raw) : {};
    } catch (_) {
      return {};
    }
  }

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
      if (cur[parts[i]] == null || typeof cur[parts[i]] !== 'object') cur[parts[i]] = {};
      cur = cur[parts[i]];
    }
    cur[parts[parts.length - 1]] = value;
  }

  function parseCsv(s) {
    return (s || '').split(/[\s,]+/).map(function (x) { return x.trim().toLowerCase(); }).filter(Boolean);
  }

  function buildCsv(set) {
    return Array.from(set).join(',\n');
  }

  function clearDeviceCache() {
    try { delete window[CACHE_KEY]; } catch (_) { window[CACHE_KEY] = null; }
  }

  function fetchDevices(apiUrl, apiToken) {
    if (window[CACHE_KEY] && Date.now() - window[CACHE_KEY].t < CACHE_TTL_MS &&
        window[CACHE_KEY].key === (apiUrl + '|' + apiToken)) {
      return Promise.resolve(window[CACHE_KEY].v);
    }
    var baseUrl = (apiUrl || '').trim().replace(/\/$/, '');
    if (!baseUrl) return Promise.reject(new Error('API URL is empty'));

    var url = baseUrl + '/devices';
    return fetch(url, { headers: { Authorization: apiToken || '' } })
      .then(function (res) {
        if (!res.ok) throw new Error('HTTP ' + res.status + ' from ' + url);
        return res.json();
      })
      .then(function (json) {
        var flat = [];
        if (json.switches) {
          Object.keys(json.switches).forEach(function (key) {
            var e = json.switches[key];
            flat.push({ slug: e.slug || ('plug-' + key), name: e.name || key, isPlug: true, rgb: false });
          });
        }
        if (json.lights) {
          Object.keys(json.lights).forEach(function (slug) {
            var e = json.lights[slug];
            flat.push({ slug: slug, name: e.name || slug, isPlug: false, rgb: !!e.rgb });
          });
        }
        window[CACHE_KEY] = { t: Date.now(), key: apiUrl + '|' + apiToken, v: flat };
        return flat;
      })
      .catch(function (err) {
        var detail = (err && (err.message || err.name)) || 'server unreachable';
        throw new Error('cannot reach ' + baseUrl + ' (' + detail + ')');
      });
  }

  function renderPicker(host, devices, currentSet, onChange, opts) {
    host.innerHTML = '';
    opts = opts || {};

    if (opts.readOnlyNote) {
      var note = document.createElement('div');
      note.className = 'picker-readonly-note';
      note.textContent = opts.readOnlyNote;
      host.appendChild(note);
    }

    var summary = document.createElement('div');
    summary.className = 'picker-summary' + (currentSet.size ? ' has-selection' : '');
    updateSummary(summary, devices, currentSet);
    host.appendChild(summary);

    function updateSummary(el, devs, set) {
      var selected = devs.filter(function (d) { return set.has(d.slug); });
      if (!selected.length) {
        el.textContent = 'No devices selected';
        el.className = 'picker-summary';
      } else {
        el.textContent = selected.map(function (d) { return d.name; }).join(', ');
        el.className = 'picker-summary has-selection';
      }
    }

    function renderGroup(devList, title) {
      if (!devList.length) return;

      var titleEl = document.createElement('div');
      titleEl.className = 'picker-group-title';
      titleEl.textContent = title + ' (' + devList.length + ')';
      host.appendChild(titleEl);

      devList.forEach(function (d) {
        var row = document.createElement('div');
        row.className = 'picker-device' + (currentSet.has(d.slug) ? ' selected' : '');

        var cb = document.createElement('input');
        cb.type = 'checkbox';
        cb.checked = currentSet.has(d.slug);
        if (opts.readOnly) cb.disabled = true;
        row.appendChild(cb);

        var nameSpan = document.createElement('span');
        nameSpan.className = 'device-name';
        nameSpan.textContent = d.name;
        row.appendChild(nameSpan);

        var slugSpan = document.createElement('span');
        slugSpan.className = 'device-slug';
        slugSpan.textContent = d.slug;
        row.appendChild(slugSpan);

        if (!opts.readOnly) {
          row.addEventListener('click', function () {
            var isSelected = currentSet.has(d.slug);
            if (isSelected) {
              currentSet.delete(d.slug);
            } else {
              currentSet.add(d.slug);
            }
            cb.checked = !isSelected;
            row.className = 'picker-device' + (!isSelected ? ' selected' : '');
            updateSummary(summary, devices, currentSet);
            onChange(currentSet);
          });
        }

        host.appendChild(row);
      });
    }

    var plugs = devices.filter(function (d) { return d.isPlug; });
    var lights = devices.filter(function (d) { return !d.isPlug; });
    renderGroup(plugs, 'Plugs');
    renderGroup(lights, 'Lights');
  }

  window.tuyaDevicePicker = function (hostId, settingPath, options) {
    var host = document.getElementById(hostId);
    if (!host) return;
    var opts = options || {};
    var state = host.__tuyaPickerState || (host.__tuyaPickerState = {
      subscribed: false,
      refreshToken: 0,
      pendingRefresh: null,
      selfWriteUntil: 0
    });

    function refresh() {
      var token = ++state.refreshToken;
      SDPIComponents.streamDeckClient.getSettings()
        .then(function (rawSettings) {
          var localSettings = unwrap(rawSettings);
          if (token !== state.refreshToken) return;
          var storedSettings = readStoredSettings();
          var useGlobal = opts.useGlobalPath && getNested(localSettings, opts.useGlobalPath) === 'global';

          return SDPIComponents.streamDeckClient.getGlobalSettings()
            .then(function (globalRaw) {
              if (token !== state.refreshToken) return;
              var globalSettings = unwrap(globalRaw);
              var globalPath = opts.globalSettingPath || 'defaultDevices.deviceSlugListString';
              var globalDeviceCsv = getNested(globalSettings, globalPath)
                || getNested(storedSettings, globalPath) || '';
              var localDeviceCsv = getNested(localSettings, settingPath) || '';
              var hasGlobalSelection = parseCsv(globalDeviceCsv).length > 0;
              var activeDeviceCsv = useGlobal && hasGlobalSelection ? globalDeviceCsv : localDeviceCsv;
              var currentSet = new Set(parseCsv(activeDeviceCsv));

              var apiUrl = opts.preferLocal
                ? (getNested(localSettings, opts.apiUrlPath || 'apiUrl') || globalSettings.apiUrl || storedSettings.apiUrl || '')
                : (globalSettings.apiUrl || storedSettings.apiUrl || getNested(localSettings, opts.apiUrlPath || 'apiUrl') || '');
              var apiToken = opts.preferLocal
                ? (getNested(localSettings, opts.apiTokenPath || 'apiToken') || globalSettings.apiToken || '')
                : (globalSettings.apiToken || getNested(localSettings, opts.apiTokenPath || 'apiToken') || '');

              return fetchDevices(apiUrl, apiToken).then(function (devices) {
                if (token !== state.refreshToken) return;
                var isReadOnly = !!(useGlobal && hasGlobalSelection && opts.readOnlyWhenGlobal);
                var readOnlyNote = '';
                if (useGlobal) {
                  readOnlyNote = hasGlobalSelection
                    ? 'Using global default devices. Edit in Global Settings.'
                    : 'Global defaults empty. Using local devices.';
                }
                renderPicker(host, devices, currentSet, function (set) {
                  var csv = buildCsv(set);
                  SDPIComponents.streamDeckClient.getSettings().then(function (s2) {
                    var cur = unwrap(s2);
                    setNested(cur, settingPath, csv);
                    state.selfWriteUntil = Date.now() + 1500;
                    SDPIComponents.streamDeckClient.setSettings(cur);
                  });
                }, {
                  readOnly: isReadOnly,
                  readOnlyNote: readOnlyNote || undefined
                });
              });
            });
        })
        .catch(function (err) {
          var detail = (err && (err.message || err.name)) || 'unknown error';
          host.innerHTML = '<div class="picker-error">Could not load devices: ' + detail +
            '. Check Global Settings (API URL + token).</div>';
        });
    }

    function scheduleRefresh() {
      if (Date.now() < state.selfWriteUntil) return;
      if (state.pendingRefresh) clearTimeout(state.pendingRefresh);
      state.pendingRefresh = setTimeout(function () {
        state.pendingRefresh = null;
        refresh();
      }, 800);
    }

    if (!state.subscribed) {
      state.subscribed = true;
      if (SDPIComponents.streamDeckClient.didReceiveGlobalSettings) {
        SDPIComponents.streamDeckClient.didReceiveGlobalSettings.subscribe(scheduleRefresh);
      }
    }

    refresh();
  };

  window.tuyaDevicePicker.clearCache = clearDeviceCache;
})();
