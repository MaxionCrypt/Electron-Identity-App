const { spawn, spawnSync } = require("child_process");
const crypto = require("crypto");
const path = require("path");

const repoRoot = process.cwd();
const port = process.env.BACKEND_PORT || "5155";
const jwtSecret = process.env.JWT_SECRET || crypto.randomBytes(48).toString("hex");
const seedAdminEmail = process.env.IDENTITY_SEED_ADMIN_EMAIL || "admin@local.dev";
const seedAdminPassword = process.env.IDENTITY_SEED_ADMIN_PASSWORD || "Admin123!";
const baseUrl = `http://127.0.0.1:${port}`;

const dotnetCmd = process.env.DOTNET || "dotnet";
const projectPath = path.join(repoRoot, "backend", "src", "Host", "Host.csproj");

console.log(`[dotnet-backend] url=${baseUrl}`);
console.log(`[identity] seeded_admin_email=${seedAdminEmail}`);
console.log(`[identity] seeded_admin_password=${seedAdminPassword}`);

const build = spawnSync(dotnetCmd, ["build", projectPath, "-m:1"], {
  cwd: repoRoot,
  stdio: "inherit",
  windowsHide: true,
  env: {
    ...process.env,
    JWT_SECRET: jwtSecret,
    IDENTITY_SEED_ADMIN_EMAIL: seedAdminEmail,
    IDENTITY_SEED_ADMIN_PASSWORD: seedAdminPassword,
    DOTNET_CLI_HOME: path.join(repoRoot, ".dotnet_cli_home"),
    MSBuildEnableWorkloadResolver: "0"
  }
});

if (build.status !== 0) {
  process.exit(build.status || 1);
}

const child = spawn(dotnetCmd, ["run", "--project", projectPath, "--no-build", "--urls", baseUrl], {
  cwd: repoRoot,
  stdio: "inherit",
  windowsHide: true,
  env: {
    ...process.env,
    JWT_SECRET: jwtSecret,
    IDENTITY_SEED_ADMIN_EMAIL: seedAdminEmail,
    IDENTITY_SEED_ADMIN_PASSWORD: seedAdminPassword,
    BACKEND_PORT: port,
    DOTNET_CLI_HOME: path.join(repoRoot, ".dotnet_cli_home"),
    MSBuildEnableWorkloadResolver: "0"
  }
});

child.on("error", (err) => {
  console.error("[dotnet-backend] failed to start:", err.message);
  process.exit(1);
});
