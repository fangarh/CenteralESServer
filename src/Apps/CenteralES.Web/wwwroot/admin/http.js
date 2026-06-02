(function () {
  function createAdminApiClient(options) {
    const getCsrfToken = options.getCsrfToken;
    const onUnauthorized = options.onUnauthorized;

    async function apiGet(url, requireAuth = true) {
      return fetchJson(url, { method: "GET" }, requireAuth);
    }

    async function apiPost(url, body) {
      return fetchJson(url, {
        method: "POST",
        headers: { "X-CSRF-Token": getCsrfToken() },
        body: JSON.stringify(body)
      });
    }

    async function fetchJson(url, requestOptions = {}, requireAuth = true) {
      const response = await fetch(url, {
        credentials: "same-origin",
        ...requestOptions,
        headers: {
          "Accept": "application/json",
          "Content-Type": "application/json",
          ...(requestOptions.headers || {})
        }
      });

      if (!response.ok) {
        const payload = await tryReadJson(response);
        const error = new Error(payload?.error?.message || `HTTP ${response.status}`);
        error.status = response.status;
        if (requireAuth && response.status === 401) {
          onUnauthorized();
        }
        throw error;
      }

      return tryReadJson(response);
    }

    async function fetchHealthJson(url) {
      const response = await fetch(url, {
        credentials: "same-origin",
        headers: { "Accept": "application/json" }
      });
      const payload = await tryReadJson(response);
      if (!response.ok && response.status !== 503) {
        const error = new Error(payload?.error?.message || `HTTP ${response.status}`);
        error.status = response.status;
        throw error;
      }
      return payload;
    }

    return {
      apiGet,
      apiPost,
      fetchJson,
      fetchHealthJson
    };
  }

  async function tryReadJson(response) {
    const text = await response.text();
    return text ? JSON.parse(text) : null;
  }

  window.CenteralESAdminHttp = {
    createAdminApiClient
  };
})();
