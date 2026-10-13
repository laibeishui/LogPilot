(() => {
  'use strict';

  let csrfTokenPromise = null;

  async function ensureCsrfToken() {
    if (!csrfTokenPromise) {
      csrfTokenPromise = fetch('/api/v1/antiforgery/token', {
        credentials: 'same-origin'
      }).then(async (res) => {
        if (!res.ok) throw new Error('无法获取防伪令牌');
        const data = await res.json();
        return data.token;
      });
    }
    return csrfTokenPromise;
  }

  async function api(path, options = {}) {
    const method = (options.method || 'GET').toUpperCase();
    const headers = Object.assign({ 'Accept': 'application/json' }, options.headers || {});

    if (options.body && !headers['Content-Type']) {
      headers['Content-Type'] = 'application/json';
    }

    if (method !== 'GET' && method !== 'HEAD') {
      headers['X-CSRF-TOKEN'] = await ensureCsrfToken();
    }

    const response = await fetch(path, {
      method,
      headers,
      credentials: 'same-origin',
      body: options.body ? JSON.stringify(options.body) : undefined
    });

    if (response.status === 401) {
      if (!location.pathname.startsWith('/account/login') && !location.pathname.startsWith('/setup')) {
        location.href = '/account/login';
      }
      const err = new Error('未登录');
      err.status = 401;
      throw err;
    }

    let payload = null;
    const text = await response.text();
    if (text) {
      try { payload = JSON.parse(text); } catch { payload = text; }
    }

    if (!response.ok) {
      const err = new Error((payload && payload.message) || ('请求失败: ' + response.status));
      err.status = response.status;
      err.payload = payload;
      throw err;
    }

    return payload;
  }

  window.LogPilotApi = { api, ensureCsrfToken };
})();
