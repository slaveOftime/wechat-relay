#!/usr/bin/env node

const path = require("path");
const fs = require("fs");
const { spawnSync } = require("child_process");

const binaryNames = {
  "linux:x64": "wechat-relay-linux-x64",
  "darwin:arm64": "wechat-relay-darwin-arm64",
  "win32:x64": "wechat-relay-win32-x64.exe",
};

const binaryName = binaryNames[`${process.platform}:${process.arch}`];

if (!binaryName) {
  console.error(
    `wechat-relay: unsupported platform/arch: ${process.platform}/${process.arch}. ` +
      "Please download a release from https://github.com/slaveoftime/wechat-relay/releases"
  );
  process.exit(1);
}

const binaryPath = path.join(__dirname, binaryName);

if (!fs.existsSync(binaryPath)) {
  console.error(
    `wechat-relay: bundled binary not found at ${binaryPath}. Try reinstalling: npm install -g @slaveoftime/wechat-relay`
  );
  process.exit(1);
}

const result = spawnSync(binaryPath, process.argv.slice(2), {
  stdio: "inherit",
  env: process.env,
});

process.exit(result.status ?? 1);
