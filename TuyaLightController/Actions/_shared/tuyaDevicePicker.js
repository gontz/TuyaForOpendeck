(function () {
  const CACHE_KEY = '__tuyaDevicePickerCache';
  const CACHE_TTL_MS = 30 * 1000;

  async function fetchDevices(apiUrl, apiToken) {
    if (window[CACHE_KEY] && Date.now() - window[CACHE_KEY].t < CACHE_TTL_MS) {
      return window[CACHE_KEY].v;
    }
    const url = (apiUrl || '').replace(/\/$/, '') + '/devices';
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
    window[CACHE_KEY] = { t: Date.now(), v: flat };
    return flat;
  }

  function parseCsv(s) {
    return (s || '').split(/[\s,]+/).map((x) => x.trim().toLowerCase()).filter(Boolean);
  }

  function buildCsv(set) {
    return Array.from(set).join(',\n');
  }

  function render(host, devices, currentSet, onChange) {
    host.innerHTML = '';
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
        row.style.padding = '2px 0';
        const cb = document.createElement('input');
        cb.type = 'checkbox';
        cb.value = d.slug;
        cb.checked = currentSet.has(d.slug);
        cb.addEventListener('change', () => {
          if (cb.checked) currentSet.add(d.slug);
          else currentSet.delete(d.slug);
          onChange(currentSet);
        });
        row.appendChild(cb);
        const txt = document.createElement('span');
        txt.style.marginLeft = '6px';
        txt.innerHTML = d.name + ' <small style="opacity:.6">' + d.slug + '</small>';
        row.appendChild(txt);
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

  window.tuyaDevicePicker = function (hostId, settingPath) {
    const host = document.getElementById(hostId);
    if (!host) return;

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
      const settings = s.settings || {};
      const currentSet = new Set(parseCsv(getNested(settings, settingPath) || ''));
      try {
        const global = await SDPIComponents.streamDeckClient.getGlobalSettings();
        const devices = await fetchDevices(global.apiUrl, global.apiToken);
        render(host, devices, currentSet, (set) => {
          const csv = buildCsv(set);
          SDPIComponents.streamDeckClient.getSettings().then((s2) => {
            const cur = s2.settings || {};
            setNested(cur, settingPath, csv);
            SDPIComponents.streamDeckClient.setSettings(cur);
          });
        });
      } catch (err) {
        showError(host, 'Could not fetch /devices: ' + err.message + '. Check Global Settings (API URL + token).');
      }
    });
  };
})();
