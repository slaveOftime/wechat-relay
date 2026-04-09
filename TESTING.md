# Manual Test Checklist

## Prerequisites
- .NET 10 SDK installed (for build) **or** download native binary from Releases
- WeChat account on a phone (for QR scan)
- `appsettings.json` in the working directory (copied from project root)

## Test Matrix

### 1. Stored Login Session (no WeChat needed if already logged in)
```
wechat-relay login
```
**Expected:** `✓ Already logged in (local session store).` or QR code flow.

### 2. QR Login (requires phone)
```
wechat-relay login --force
```
**Expected:**
- QR URL printed in terminal (boxed)
- No browser auto-open
- Wait for scan → `✓ Login successful!`
- Credentials saved to `%APPDATA%\wechat-relay\session-state.json`

### 3. List Send-to Candidates
```
wechat-relay list-send-to
```
**Expected:** Lists `UserId` and `ToUsers` from config/local session state.

### 4. Send Message (requires context token)
```
# Default channel (uses cached UserId)
wechat-relay send --text "test"

# Specific target
wechat-relay send o9cq8...@im.wechat --text "test"

# Image
wechat-relay send o9cq8...@im.wechat --image .\sample.jpg

# Audio / voice
wechat-relay send o9cq8...@im.wechat --audio .\sample.silk --audio-playtime-ms 4210

# Via stdin
echo hello | wechat-relay send
```
**Expected:** `✓ Message sent...`, `✓ Image sent...`, or `✓ Audio sent...`; otherwise an error with hint about context token.

### 5. Listen + Hook (requires inbound message)

**Step A — Configure hook:**
Edit `appsettings.json`:
```json
{
  "Hook": {
    "Command": "echo {payload}",
    "WorkingDirectory": null
  }
}
```

**Step B — Start listener:**
```
wechat-relay listen
```
**Expected:**
- `=== Listening for WeChat Messages ===`
- Long-poll starts against `getupdates`
- Console waits for inbound messages

**Step C — Send a message to the bot from WeChat on your phone.**

**Expected output:**
```
[15:23:01] seq=42 from=o9cq8...@im.wechat type=1 text="hello"
[15:23:15] seq=43 from=o9cq8...@im.wechat type=1 [image]
[15:23:22] seq=44 from=o9cq8...@im.wechat type=1 text="我下午三点到。" [audio 4210ms]
```

**Hook payload (echoed to console):**
```json
{"seq":42,"message_id":...,"from_user_id":"o9cq8...","to_user_id":"...","create_time_ms":...,"session_id":"","message_type":1,"text":"hello","summary":"text=\"hello\" [image]","items":[{"item_type":2,"kind":"image","download_url":"https://novac2c.cdn.weixin.qq.com/c2c/download?encrypted_query_param=...","encrypt_query_param":"...","aes_key":"..."}],"context_token":"AARz..."}
```

**Step D — Ctrl+C to stop.**
**Expected:** Clean exit. Next `listen` resumes without losing messages (disk queue).

### 6. Crash Recovery (queue persistence)
1. Start `wechat-relay listen`
2. Send a WeChat message
3. Kill the process (Ctrl+C) immediately after it prints the message
4. Run `wechat-relay listen` again
**Expected:** `Drained N persisted messages from queue` → hook fires for the killed message.

### 7. Global Tool
```
dotnet tool install -g --add-source ./artifacts/packages wechat-relay
wechat-relay
```
**Expected:** Same behavior as `dotnet run --`.

## Known Constraints
- **context_token required for send**: The WeChat ilink API only allows replying to users who have messaged the bot first. Run `listen` first, receive a message, then `send` works automatically (token cached).
- **QR expiry**: QR codes expire after ~3 minutes. Have your phone ready before running `login --force`.
- **Hook runs async**: The hook command runs in a background thread. A disk-backed JSONL queue (`pending-messages.jsonl`) ensures messages aren't lost if the process crashes.
