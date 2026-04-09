# wechat-relay

> Your personal WeChat account, exposed as a tiny event pipe.
> Scan QR. Listen forever. Ship payloads anywhere.

[![CI](https://github.com/slaveoftime/wechat-relay/actions/workflows/ci.yml/badge.svg)](https://github.com/slaveoftime/wechat-relay/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/wechat-relay.svg?color=blue&logo=nuget)](https://www.nuget.org/packages/wechat-relay)
[![MIT](https://img.shields.io/github/license/slaveoftime/wechat-relay)](LICENSE)
[![dotnet tool](https://img.shields.io/badge/dotnet--tool-install-512bd4)](#install)

`wechat-relay` is a minimal CLI that turns a WeChat personal account into a programmable message bridge.

- QR login with local session persistence
- Long-poll listener with crash-safe queue replay
- Hook command execution for every inbound message
- Reply from the CLI with text, image, or audio
- NuGet tool, npm wrapper, and native AOT binaries

## Install

### npm wrapper

```bash
npm install -g @slaveoftime/wechat-relay
wechat-relay
```

The npm package ships a bundled native binary for:

- `linux-x64`
- `win32-x64`
- `darwin-arm64`

### .NET global tool

```bash
dotnet tool install -g wechat-relay
wechat-relay
```

### Native binary

Grab a release artifact and run it directly:

- `wechat-relay-{version}-linux-x64.tar.gz`
- `wechat-relay-{version}-win-x64.zip`
- `wechat-relay-{version}-osx-arm64.tar.gz`

Native builds are self-contained. No SDK required.

## Boot Sequence

```bash
# 1. Pair the account
wechat-relay login

# 2. Start the stream
wechat-relay listen

# 3. Reply with text
wechat-relay send --text "hello from the terminal"

# 4. Reply with media
wechat-relay send friend@im.wechat --image ./cat.jpg
wechat-relay send friend@im.wechat --audio ./reply.silk --audio-playtime-ms 4210
```

The first time you reply to a user, WeChat expects a `context_token` from an inbound message.
Translation: run `listen`, let them message you once, then `send` works.

## Commands

| Command | What it does |
|---|---|
| `login` | Starts QR login, or reuses the stored local session |
| `login --force` | Clears stored session state and pairs again |
| `listen` | Long-polls WeChat and fires your hook for each inbound message |
| `listen --hook "..."` | Overrides `Hook:Command` for the current run |
| `list-send-to` | Prints send targets from `WeChat:UserId` and `WeChat:ToUsers` |
| `send [target] --text "..."` | Sends a text message |
| `send [target] --image ./file.jpg` | Uploads and sends an image |
| `send [target] --audio ./file.silk` | Uploads and sends audio or voice |
| `--verbose` | Enables debug logging on any command |

`send` accepts exactly one payload mode at a time: `--text`, `--image`, or `--audio`.

Audio flags currently supported:

```bash
wechat-relay send friend@im.wechat \
  --audio ./reply.ogg \
  --audio-format ogg \
  --audio-sample-rate 16000 \
  --audio-bits-per-sample 16 \
  --audio-playtime-ms 4210
```

Supported audio format hints: `pcm`, `wav`, `adpcm`, `feature`, `speex`, `amr`, `silk`, `mp3`, `ogg`.

## Config

Put `appsettings.json` next to the executable or run from the project directory.

```json
{
  "WeChat": {
    "BaseUrl": "https://ilinkai.weixin.qq.com/",
    "BotType": "3",
    "UserId": "self@im.wechat",
    "ToUsers": "friend1@im.wechat,friend2@im.wechat"
  },
  "Hook": {
    "Command": "echo {payload}",
    "WorkingDirectory": null
  }
}
```

Notes:

- `Hook:Command` defaults to `echo`
- `listen --hook` overrides `Hook:Command` without editing config
- `UserId` is the default `send` target
- `ToUsers` adds extra IDs for `list-send-to`

## Hook Mode

Every inbound message is queued, persisted, and then passed to your hook command as JSON.

Minimal example:

```json
{
  "seq": 42,
  "message_id": 7447467781622590088,
  "from_user_id": "friend@im.wechat",
  "to_user_id": "bot@im.bot",
  "create_time_ms": 1775614686361,
  "message_type": 1,
  "text": "hello",
  "summary": "text=\"hello\" [image] [audio 4210ms]",
  "items": [
    {
      "item_type": 1,
      "kind": "text",
      "text": "hello"
    },
    {
      "item_type": 2,
      "kind": "image",
      "preview_url": "https://...",
      "download_url": "https://novac2c.cdn.weixin.qq.com/c2c/download?...",
      "encrypt_query_param": "...",
      "aes_key": "..."
    }
  ],
  "context_token": "AARzJWAFAAABAAAA..."
}
```

Storage lives under your application data directory:

- `session-state.json` stores login state and cached context tokens
- `pending-messages.jsonl` stores queued hook work for crash recovery

If the process dies after receipt but before hook execution, the next `listen` run drains the queue and replays the pending payloads.

## Console Vibe

Typical listener output:

```text
Listening

Bridge WeChat messages to any webhook
Press Ctrl+C to stop.

[14:23:01] 42 friend@im.wechat type=1 text="hello"
[14:23:15] 43 friend@im.wechat type=1 [image]
[14:23:22] 44 friend@im.wechat type=1 text="see attached" [audio 4210ms]
```

## Build From Source

```bash
git clone https://github.com/slaveoftime/wechat-relay.git
cd wechat-relay/WeChatRelay

# local build
dotnet build

# package as a dotnet tool
dotnet pack -c Release

# native AOT
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishAot=true
dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishAot=true
dotnet publish -c Release -r osx-arm64 --self-contained true -p:PublishAot=true
```

Target framework: `.NET 10`.

## License

[MIT](LICENSE)
