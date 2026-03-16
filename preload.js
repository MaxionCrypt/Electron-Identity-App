const { contextBridge, ipcRenderer } = require("electron");

function getBackendBaseUrl() {
  const port = process.env.BACKEND_PORT || "5155";
  return `http://127.0.0.1:${port}`;
}

async function requestJson(path, { method = "GET", token, payload } = {}) {
  const url = `${getBackendBaseUrl()}${path}`;
  const headers = {
    Accept: "application/json"
  };

  if (payload !== undefined) {
    headers["Content-Type"] = "application/json";
  }

  if (token) {
    headers.Authorization = `Bearer ${token}`;
  }

  const response = await fetch(url, {
    method,
    headers,
    body: payload !== undefined ? JSON.stringify(payload) : undefined
  });

  const bodyText = await response.text();
  let body = null;
  if (bodyText) {
    try {
      body = JSON.parse(bodyText);
    } catch (_) {
      body = null;
    }
  }

  if (!response.ok) {
    const message = body && typeof body.error === "string"
      ? body.error
      : `HTTP ${response.status}`;
    throw new Error(message);
  }

  return body;
}

contextBridge.exposeInMainWorld("electronAPI", {
  ping: () => ipcRenderer.invoke("ping")
});

contextBridge.exposeInMainWorld("backendAPI", {
  register: async (payload) => requestJson("/api/auth/register", { method: "POST", payload }),
  login: async (payload) => requestJson("/api/auth/login", { method: "POST", payload }),
  me: async (token) => requestJson("/api/auth/me", { token }),
  adminUsers: async (token) => requestJson("/api/admin/users", { token })
});
