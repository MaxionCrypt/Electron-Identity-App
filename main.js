const { app, BrowserWindow, ipcMain } = require("electron");
const { spawn } = require("child_process");
const crypto = require("crypto");
const http = require("http");
const path = require("path");

let dotnetBackendProcess = null;
let dotnetBackendBaseUrl = null;

function httpGetOk(url) {
  return new Promise((resolve, reject) => {
    const req = http.get(url, (res) => {
      const ok = res.statusCode && res.statusCode >= 200 && res.statusCode < 300;
      res.resume();
      resolve(ok);
    });
    req.on("error", reject);
    req.setTimeout(1500, () => {
      req.destroy(new Error("timeout"));
    });
  });
}

async function waitForHealthy(url, timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  // eslint-disable-next-line no-constant-condition
  while (true) {
    try {
      const ok = await httpGetOk(url);
      if (ok) return;
    } catch (_) {
      // ignore, keep retrying
    }

    if (Date.now() > deadline) {
      throw new Error(`Timed out waiting for backend health at ${url}`);
    }

    await new Promise((r) => setTimeout(r, 250));
  }
}

async function startDotnetBackend() {
  const port = process.env.BACKEND_PORT || "5155";
  const jwtSecret = process.env.JWT_SECRET || crypto.randomBytes(48).toString("hex");
  const seedAdminEmail = process.env.IDENTITY_SEED_ADMIN_EMAIL || "admin@local.dev";
  const seedAdminPassword = process.env.IDENTITY_SEED_ADMIN_PASSWORD || "Admin123!";
  const baseUrl = `http://127.0.0.1:${port}`;

  process.env.JWT_SECRET = jwtSecret;
  process.env.IDENTITY_SEED_ADMIN_EMAIL = seedAdminEmail;
  process.env.IDENTITY_SEED_ADMIN_PASSWORD = seedAdminPassword;
  process.env.BACKEND_PORT = port;

  const dotnetCmd = process.env.DOTNET || "dotnet";
  const projectPath = path.join(__dirname, "backend", "src", "Host", "Host.csproj");

  dotnetBackendProcess = spawn(
    dotnetCmd,
    ["run", "--project", projectPath, "--urls", baseUrl],
    {
      cwd: __dirname,
      stdio: "inherit",
      windowsHide: true,
      env: {
        ...process.env,
        JWT_SECRET: jwtSecret,
        IDENTITY_SEED_ADMIN_EMAIL: seedAdminEmail,
        IDENTITY_SEED_ADMIN_PASSWORD: seedAdminPassword,
        BACKEND_PORT: port,
        MSBuildEnableWorkloadResolver: "0",
        DOTNET_CLI_HOME: path.join(app.getPath("userData"), ".dotnet_cli_home")
      }
    }
  );

  dotnetBackendProcess.on("exit", (code, signal) => {
    console.log(`[dotnet-backend] exited (code=${code}, signal=${signal})`);
  });

  await waitForHealthy(`${baseUrl}/health`, 20_000);
  dotnetBackendBaseUrl = baseUrl;
}

function createWindow() {
  const mainWindow = new BrowserWindow({
    width: 1000,
    height: 700,
    webPreferences: {
      preload: path.join(__dirname, "preload.js"),
      contextIsolation: true,
      nodeIntegration: false
    }
  });

  if (dotnetBackendBaseUrl) {
    mainWindow.loadURL(dotnetBackendBaseUrl);
  } else {
    mainWindow.loadFile("index.html");
  }
}

app.whenReady().then(() => {
  ipcMain.handle("ping", () => "Pong from Electron main process");
  startDotnetBackend()
    .catch((err) => console.warn("[dotnet-backend] failed to start:", err.message))
    .finally(() => createWindow());

  app.on("activate", () => {
    if (BrowserWindow.getAllWindows().length === 0) {
      createWindow();
    }
  });
});

app.on("before-quit", () => {
  if (dotnetBackendProcess) {
    try {
      dotnetBackendProcess.kill();
    } catch (_) {
      // Best-effort: ignore.
    }
  }
});

app.on("window-all-closed", () => {
  if (process.platform !== "darwin") {
    app.quit();
  }
});
