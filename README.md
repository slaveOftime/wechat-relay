# wechat-relay

> Bridge **WeChat personal account** messages to any webhook, script, or pipeline.  
> Zero dependencies beyond .NET 10. Native AOT binaries for Linux, macOS & Windows.

[![CI](https://github.com/slaveoftime/wechat-relay/actions/workflows/ci.yml/badge.svg)](https://github.com/slaveoftime/wechat-relay/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/wechat-relay.svg?color=blue&logo=nuget)](https://www.nuget.org/packages/wechat-relay)
[![MIT](https://img.shields.io/github/license/slaveoftime/wechat-relay)](LICENSE)
[![dotnet tool](https://img.shields.io/badge/dotnet--tool-install-512bd4)](#install)

## Features

| | |
|---|---|
| 🔐 **QR Login** | Scan once, credentials saved in a local JSON session file. No expiry until the server invalidates the session. |
| 📡 **Persistent Listener** | Long-poll WeChat messages. Survives restarts via disk-backed queue. |
| 🔗 **Configurable Hooks** | Every inbound message triggers your command (default: `echo`). Passes JSON metadata. |
| 📤 **Send Messages** | Reply to any user via CLI. Context tokens cached automatically. |
| 🏗️ **Native AOT** | Single-file, self-contained binaries — no runtime needed. |
| 🐧 **Cross-platform** | `linux-x64`, `win-x64`, `osx-arm64`. |

## Install

### As a .NET global tool

### As an npm package

```bash
npm install -g @slaveoftime/wechat-relay
wechat-relay          # runs the bundled native binary
```

```bash
dotnet tool install -g wechat-relay
wechat-relay          # prints usage
```

### Or grab a native binary

Download from [Releases](https://github.com/slaveoftime/wechat-relay/releases):

| Platform | Asset |
|----------|-------|
| Linux x64 | `wechat-relay-v{version}-linux-x64.tar.gz` |
| Windows x64 | `wechat-relay-v{version}-win-x64.zip` |
| macOS ARM64 | `wechat-relay-v{version}-osx-arm64.tar.gz` |

Extract and run — **no .NET SDK required**.

## Quick Start

```bash
# 1. Login — scan QR with WeChat on your phone
wechat-relay login

# 2. Start listening — messages print to console + trigger your hook
wechat-relay listen

# 3. Send a message
wechat-relay send --text "Hello from CLI!"
```

## Commands

### `login`

QR code login. Credentials and reply-session tokens are saved in a local JSON session file under your application data directory. They stay available until the server-side session actually expires.

```
wechat-relay login           # use cached or start QR flow
wechat-relay login --force   # force new QR login
```

No browser auto-open — the QR URL is printed directly in your terminal for easy copy/paste.

### `listen`

Runs until `Ctrl+C`. Logs each inbound message to console and fires your configured hook command **asynchronously** (non-blocking).

```bash
wechat-relay listen
```

**Output:**
```
=== Listening for WeChat Messages ===
Press Ctrl+C to stop.

[14:23:01] seq=42 from=o9cq8...@im.wechat type=1 text="你好"
[14:23:15] seq=43 from=o9cq8...@im.wechat type=1 text="在吗？"
```

**Hook payload** (JSON passed to your command):
```json
{
  "seq": 42,
  "message_id": 7447467781622590088,
  "from_user_id": "<user-id>@im.wechat",
  "to_user_id": "<bot-id>@im.bot",
  "create_time_ms": 1775614686361,
  "session_id": "",
  "message_type": 1,
  "text": "你好",
  "context_token": "AARzJWAFAAABAAAA..."
}
```

**Persistent queue:** Messages are written to `~/.wechat-relay/pending-messages.jsonl` before hook execution. If the process crashes, pending messages are replayed on next `listen`.

### `list-send-to`

Show all users you can send messages to.

```bash
wechat-relay list-send-to
```

### `send`

Send a text message to a WeChat user.

```bash
# Send to default channel (logged-in user)
wechat-relay send --text "Hello!"

# Send to a specific user
wechat-relay send user@im.wechat --text "Hi there"

# Pipe from stdin
echo "piped message" | wechat-relay send
```

> **Note:** The WeChat ilink API requires a `context_token` from a prior inbound message. Run `listen` first and have the user message you, then `send` will use the cached token automatically.

## Configuration

Edit `appsettings.json`:

```json
{
  "AppSettings": {
    "LogPath": "logs/wechat-relay.log",
    "Verbose": false
  },
  "WeChat": {
    "BaseUrl": "https://ilinkai.weixin.qq.com/",
    "BotType": "3",
    "UserId": "your-user@im.wechat",
    "ToUsers": "user1@im.wechat,user2@im.wechat"
  },
  "Hook": {
    "Command": "node /path/to/your/hook.js {payload}",
    "WorkingDirectory": "/path/to/your/project"
  }
}
```

### Hook Examples

**Echo (default):**
```json
{ "Command": "echo" }
```

**Call a Node.js script:**
```json
{ "Command": "node /opt/hooks/wechat.js {payload}" }
```

**Curl to a webhook:**
```json
{ "Command": "curl -X POST https://hooks.example.com/wechat -H Content-Type:application/json -d '{payload}'" }
```

**Run a Python script:**
```json
{ "Command": "python3 /opt/hooks/handler.py" }
```

The `{payload}` placeholder is replaced with the raw JSON. Omit it to pass the JSON as a quoted argument.

## Architecture

```
┌─────────────────────────────────────────────┐
│                 wechat-relay                │
│                                             │
│  ┌───────────┐     ┌────────────────────┐   │
│  │  listen   │───▶│   Inbound Messages  │   │
│  │ (long-    │    │                      │  │
│  │  poll)    │    │  ┌──┐ ┌──┐ ┌──┐      │  │
│  └───────────┘    │  │M1│ │M2│ │M3│ ...  │  │
│                   │  └┬─┘ └┬─┘ └┬─┘      │  │
│                   └───┼────┼────┼────────┘  │
│                       │    │    │           │
│               ┌───────┘    │    └─────┐     │
│               ▼            ▼          ▼     │
│  ┌──────────────────────────────────────┐   │
│  │       Persistent Queue (JSONL)       │   │
│  └──────────────────┬───────────────────┘   │
│                     │                       │
│              ┌──────▼───────┐               │
│              │  Hook Runner │               │
│  ┌─────────▶ │  (async)     │──────────┐   │
│  │           └──────────────┘           │   │
│  │                                      │   │
│  │  cmd: echo / node / curl / python    │   │
│  └──────────────────────────────────────┘   │
│                                             │
│  ┌──────────────────────────────────────┐   │
│  │      Local Session Store             │   │
│  │   (%APPDATA%/wechat-relay)           │   │
│  │  - login credentials                 │   │
│  │  - reply context tokens              │   │
│  │  - session-state.json                │   │
│  └──────────────────────────────────────┘   │
└─────────────────────────────────────────────┘
```

## Build from Source

```bash
git clone https://github.com/slaveoftime/wechat-relay.git
cd wechat-relay/WeChatRelay

# Debug build
dotnet build

# Release build
dotnet build -c Release

# Pack as NuGet tool
dotnet pack -c Release

# Native AOT (single file, no runtime needed)
dotnet publish -c Release -r win-x64   --self-contained -p:PublishAot=true
dotnet publish -c Release -r linux-x64 --self-contained -p:PublishAot=true
dotnet publish -c Release -r osx-arm64 --self-contained -p:PublishAot=true
```

## License

[MIT](LICENSE)
