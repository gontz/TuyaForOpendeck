(function () {
  const CACHE_KEY = '__tuyaDevicePickerCache';
  const CACHE_TTL_MS = 30 * 1000;
  const STORAGE_KEY = 'tuyaPluginGlobalSettings';

  function unwrapSettings(payload) {
    if (!payload) return {};
    if (payload.settings && typeof payload.settings === 'object') return payload.settings;
    if (payload.payload && payload.payload.settings && typeof payload.payload.settings === 'object') return payload.payload.settings;
    return payload;
  }

  function readStoredSettings() {
    try {
      const raw = window.localStorage.getItem(STORAGE_KEY);
      return raw ? JSON.parse(raw) : {};
    } catch (_) {
      return {};
    }
  }

  async function fetchDevices(apiUrl, apiToken) {
    if (window[CACHE_KEY] && Date.now() - window[CACHE_KEY].t < CACHE_TTL_MS && window[CACHE_KEY].key === (apiUrl + '|' + apiToken)) {
      return window[CACHE_KEY].v;
    }
    const baseUrl = (apiUrl || '').trim().replace(/\/$/, '');
    if (!baseUrl) {
      throw new Error('API URL is empty');
    }
    const url = baseUrl + '/devices';
    const res = await fetch(url, { headers: { Authorization: apiToken || '' } });
    if (!res.ok) {
      throw new Error('HTTP ' + res.status);
    }
    const json = await res.json();
    const flat = [];
    if (json.switches) {
      for (const key of Object.keys(json.switches)) {
        const e = json.switches[key];
        flat.push({ slug: e.slug || ('plug-' + key), name: e.name || key, isPlug: true, rgb: false });
      }
    }
    if (json.lights) {
      for (const slug of Object.keys(json.lights)) {
        const e = json.lights[slug];
        flat.push({ slug, name: e.name || slug, isPlug: false, rgb: !!e.rgb });
      }
    }
    window[CACHE_KEY] = { t: Date.now(), key: apiUrl + '|' + apiToken, v: flat };
    return flat;
  }

  function parseCsv(s) {
    return (s || '').split(/[\s,]+/).map((x) => x.trim().toLowerCase()).filter(Boolean);
  }

  function buildCsv(set) {
    return Array.from(set).join(',\n');
  }

  function selectedSummary(devices, currentSet) {
    const selected = devices.filter((d) => currentSet.has(d.slug));
    if (!selected.length) return 'No devices selected';
    return 'Selected: ' + selected.map((d) => d.name + ' (' + d.slug + ')').join(', ');
  }

  function applySelectionStyles(row, txt, badge, selected) {
    row.style.background = selected ? 'rgba(80,160,255,.20)' : 'rgba(255,255,255,.03)';
    row.style.border = selected
      ? '1px solid rgba(80,160,255,.6)'
      : '1px solid rgba(255,255,255,.08)';
    row.style.boxShadow = selected
      ? 'inset 0 0 0 1px rgba(140,194,255,.22)'
      : 'none';
    txt.style.color = selected ? '#eef6ff' : '';
    txt.style.fontWeight = selected ? '600' : '400';
    badge.style.display = selected ? 'inline-block' : 'none';
  }

  function render(host, devices, currentSet, onChange, options) {
    host.innerHTML = '';
    const opts = options || {};
    if (opts.summaryText) {
      const summary = document.createElement('div');
      summary.textContent = opts.summaryText;
      summary.style.fontSize = '11px';
      summary.style.opacity = '.8';
      summary.style.marginBottom = '6px';
      host.appendChild(summary);
    }
    const selection = document.createElement('div');
    selection.textContent = selectedSummary(devices, currentSet);
    selection.style.fontSize = '11px';
    selection.style.marginBottom = '8px';
    selection.style.padding = '6px';
    selection.style.border = currentSet.size
      ? '1px solid rgba(80,160,255,.65)'
      : '1px solid rgba(255,255,255,.15)';
    selection.style.borderRadius = '4px';
    selection.style.background = currentSet.size
      ? 'rgba(80,160,255,.18)'
      : 'rgba(255,255,255,.04)';
    selection.style.color = currentSet.size ? '#dcecff' : '';
    selection.style.fontWeight = currentSet.size ? '600' : '400';
    host.appendChild(selection);
    const refreshSummary = () => {
      selection.textContent = selectedSummary(devices, currentSet);
      selection.style.border = currentSet.size
        ? '1px solid rgba(80,160,255,.65)'
        : '1px solid rgba(255,255,255,.15)';
      selection.style.background = currentSet.size
        ? 'rgba(80,160,255,.18)'
        : 'rgba(255,255,255,.04)';
      selection.style.color = currentSet.size ? '#dcecff' : '';
      selection.style.fontWeight = currentSet.size ? '600' : '400';
    };
    const make = (group, title) => {
      const wrap = document.createElement('details');
      wrap.open = true;
      wrap.style.marginBottom = '6px';
      const sum = document.createElement('summary');
      sum.textContent = title + ' (' + group.length + ')';
      sum.style.fontWeight = 'bold';
      wrap.appendChild(sum);
      for (const d of group) {
        const row = document.createElement('label');
        row.style.display = 'block';
        row.style.padding = '6px 8px';
        row.style.margin = '4px 0';
        row.style.borderRadius = '4px';
        row.style.cursor = opts.readOnly ? 'default' : 'pointer';
        const cb = document.createElement('input');
        cb.type = 'checkbox';
        cb.value = d.slug;
        cb.checked = currentSet.has(d.slug);
        cb.disabled = !!opts.readOnly;
        cb.style.marginRight = '8px';
        cb.style.accentColor = '#4da3ff';
        cb.style.transform = 'scale(1.15)';
        cb.style.verticalAlign = 'middle';
        const applySelection = (checked) => {
          if (checked) currentSet.add(d.slug);
          else currentSet.delete(d.slug);
          applySelectionStyles(row, txt, badge, checked);
          refreshSummary();
          onChange(currentSet);
        };
        cb.addEventListener('change', () => {
          applySelection(cb.checked);
        });
        if (!opts.readOnly) {
          row.addEventListener('click', (ev) => {
            if (ev.target === cb) {
              return;
            }
            ev.preventDefault();
            cb.checked = !cb.checked;
            applySelection(cb.checked);
          });
        }
        row.appendChild(cb);
        const txt = document.createElement('span');
        txt.innerHTML = d.name + ' <small style="opacity:.6">' + d.slug + '</small>';
        row.appendChild(txt);
        const badge = document.createElement('span');
        badge.textContent = ' SELECTED';
        badge.style.marginLeft = '8px';
        badge.style.fontSize = '10px';
        badge.style.opacity = '1';
        badge.style.color = '#dcecff';
        badge.style.background = 'rgba(80,160,255,.38)';
        badge.style.border = '1px solid rgba(140,194,255,.65)';
        badge.style.borderRadius = '999px';
        badge.style.padding = '1px 6px';
        badge.style.letterSpacing = '.03em';
        row.appendChild(badge);
        applySelectionStyles(row, txt, badge, currentSet.has(d.slug));
        wrap.appendChild(row);
      }
      return wrap;
    };
    const plugs = devices.filter((d) => d.isPlug);
    const lights = devices.filter((d) => !d.isPlug);
    if (plugs.length) host.appendChild(make(plugs, 'Plugs'));
    if (lights.length) host.appendChild(make(lights, 'Lights'));
  }

  function showError(host, msg) {
    host.innerHTML = '<div style="background:#a33;color:#fff;padding:6px;border-radius:3px">' + msg + '</div>';
  }

  window.tuyaDevicePicker = function (hostId, settingPath, options) {
    const host = document.getElementById(hostId);
    if (!host) return;
    const opts = options || {};

    function getNested(obj, path) {
      return path.split('.').reduce((o, k) => (o && o[k] != null ? o[k] : ''), obj || {});
    }

    function setNested(obj, path, value) {
      const keys = path.split('.');
      let cur = obj;
      for (let i = 0; i < keys.length - 1; i++) {
        if (cur[keys[i]] == null || typeof cur[keys[i]] !== 'object') cur[keys[i]] = {};
        cur = cur[keys[i]];
      }
      cur[keys[keys.length - 1]] = value;
    }

    SDPIComponents.streamDeckClient.getSettings().then(async (s) => {
      const localSettings = unwrapSettings(s);
      const storedSettings = readStoredSettings();
      const useGlobal = opts.useGlobalPath && getNested(localSettings, opts.useGlobalPath) === 'global';
      try {
        const globalRaw = await SDPIComponents.streamDeckClient.getGlobalSettings();
        const globalSettings = unwrapSettings(globalRaw);
        const globalDeviceCsv = getNested(globalSettings, opts.globalSettingPath || 'defaultDevices.deviceSlugListString')
          || getNested(storedSettings, opts.globalSettingPath || 'defaultDevices.deviceSlugListString')
          || '';
        const localDeviceCsv = getNested(localSettings, settingPath) || '';
        const hasGlobalSelection = parseCsv(globalDeviceCsv).length > 0;
        const activeDeviceCsv = useGlobal && hasGlobalSelection ? globalDeviceCsv : localDeviceCsv;
        const currentSet = new Set(parseCsv(activeDeviceCsv));
        const apiUrl = opts.preferLocal
          ? (getNested(localSettings, opts.apiUrlPath || 'apiUrl') || globalSettings.apiUrl || storedSettings.apiUrl || '')
          : (globalSettings.apiUrl || storedSettings.apiUrl || getNested(localSettings, opts.apiUrlPath || 'apiUrl') || '');
        const apiToken = opts.preferLocal
          ? (getNested(localSettings, opts.apiTokenPath || 'apiToken') || globalSettings.apiToken || storedSettings.apiToken || '')
          : (globalSettings.apiToken || storedSettings.apiToken || getNested(localSettings, opts.apiTokenPath || 'apiToken') || '');
        const devices = await fetchDevices(apiUrl, apiToken);
        render(host, devices, currentSet, (set) => {
          const csv = buildCsv(set);
          SDPIComponents.streamDeckClient.getSettings().then((s2) => {
            const cur = unwrapSettings(s2);
            setNested(cur, settingPath, csv);
            SDPIComponents.streamDeckClient.setSettings(cur);
          });
        }, {
          readOnly: !!(useGlobal && hasGlobalSelection && opts.readOnlyWhenGlobal),
          summaryText: useGlobal
            ? (hasGlobalSelection
              ? 'Using global default devices below. Edit them on the Global Settings action.'
              : 'Global default devices are empty. Using this action\'s local device list until globals are configured.')
            : ''
        });
      } catch (err) {
        showError(host, 'Could not fetch /devices: ' + err.message + '. Check Global Settings (API URL + token).');
      }
    });
  };
})();
