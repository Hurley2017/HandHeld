# HandHeld — Project Memory / Status

_Created: 2026-08-26 (after long debugging session, context was exhausted)_

## What this is

Local-network game streaming: Windows PC (host, RTX 5060 / i5-14400F / 32GB) → Samsung S24 (client, One UI 8.5 / Android 16). Own Moonlight/Sunshine replacement. Everything on **D:** drive only.

## Repo layout

- `D:\Projects\HandHeld\HandHeld Icon.png` — the single pixel-art gamepad icon (blue gamepad, green D-pad + Wi-Fi arcs on navy). Used everywhere (host tray/window/taskbar, Android launcher).
- `host\` — .NET 8 WPF solution (HandHeld.sln):
  - `src\HandHeld.Host` — WPF tray app, HostWindow (clients list, game, start/stop), start-host launcher
  - `src\HandHeld.Host.Core` — HostCore (WS server, discovery, session mgmt), Video (DesktopCapture DXGI, GdiDisplayCapture, H264Encoder, VideoSender, StreamSession, DisplayManager), Audio (LoopbackCapture, AudioStreamer), Input (InputReceiver), Launcher (GameScanner, GameLauncher, GameIconProvider)
- `client\` — Kotlin + Jetpack Compose Gradle project (app module), `gradle\libs.versions.toml`
- `scripts\` — setup-all.ps1 (toolchains to D:\Dev), env.ps1, build-all.ps1, run-host.ps1, start-host.cmd, install-apk.ps1
- `docs\` — architecture.md, protocol.md (ports: UDP 45310 discovery, WS 45320 control, UDP 45330 video, UDP 45340 audio, UDP 45350 input; 14-byte video header with 16-bit fragId/fragCount)
- `artifacts\` — HandHeld-debug.apk, host-publish, host-stream.log (diagnostic)

## Toolchains (all on D:, nothing on C:)

- .NET 8 SDK: `D:\Dev\dotnet` (dotnet.exe; MUST set DOTNET_ROOT=D:\Dev\dotnet to run host — machine env not set, user-level DOTNET_ROOT IS set)
- JDK 17 (Temurin): `D:\Dev\jdks\jdk-17`
- Android SDK: `D:\Dev\Android\sdk` (platforms;android-36, build-tools;36.0.0, platform-tools) — licenses pre-accepted
- Gradle 8.9: `D:\Dev\gradle-dist\gradle-8.9\bin\gradle.bat`; GRADLE_USER_HOME=D:\Dev\gradle-home (was D:\Dev\gradle in older builds)
- NuGet: D:\Dev\nuget
- FFmpeg: `D:\Dev\ffmpeg-dist\ffmpeg-9.0.1-essentials_build\bin\ffmpeg.exe` (used by H264Encoder via subprocess, NVENC)
- ADB: `D:\Dev\Android\sdk\platform-tools\adb.exe`

## Virtual Display Driver (VDD)

- Installed from VirtualDrivers/Virtual-Display-Driver release 25.7.23 into `D:\Dev\vdd\` (extracted) + `D:\Dev\vdd-control\` (VDD Control.exe, Dependencies\devcon.exe, vdd_settings.xml)
- Device: `ROOT\DISPLAY\0000` (enabled via pnputil; devcon restart)
- Instance ID: ROOT\DISPLAY\0000
- vdd_settings.xml controls resolutions (edited to add 2340x1080, 3120x1440, 1920x1080@60 etc.)
- The VDD appears as `\\.\DISPLAY5`, "Virtual Display Driver". **DXGI Desktop Duplication fails on it** (E_INVALIDARG / NRE) — but **GDI BitBlt capture works (GdiDisplayCapture)**.
- Host logic: SetVirtualDisplayEnabled(true/false) via pnputil (fire-and-forget, never blocks); on launch → enable VDD, SetVirtualDisplayResolution (enumerates real modes, picks closest), SetPrimaryDisplay(vd); on stop/disconnect → disable VDD.
- NOTE: VDD currently stays ENABLED (disable at startup didn't stick / pin states). Virtual display is primary at 1920x1080 (or 800x600 initially).

## Host build/run

- Build: `dotnet build D:\Projects\HandHeld\host\HandHeld.sln -c Release` with DOTNET_ROOT/DOTNET/NUGET set
- Output: `host\src\HandHeld.Host\bin\Release\net8.0-windows\HandHeld.Host.exe` (WinExe GUI — fixed console flash by OutputType=WinExe)
- Run: needs DOTNET_ROOT=D:\Dev\dotnet; desktop shortcut "HandHeld Host.lnk" → exe directly; user-level DOTNET_ROOT set so it works
- app.ico created from HandHeld Icon.png, embedded + copied to output (taskbar icon)
- ffmpeg.exe NOT copied next to exe — encoder falls back to D:\Dev path

## Client build/install

- Build: gradle.bat assembleDebug with JAVA_HOME=D:\Dev\jdks\jdk-17, ANDROID_HOME=D:\Dev\Android (sdk root is D:\Dev\Android\sdk), GRADLE_USER_HOME=D:\Dev\gradle-home
- APK: `client\app\build\outputs\apk\debug\app-debug.apk` → copied to `artifacts\HandHeld-debug.apk`
- Install: `adb install -r` (wireless: mDNS finds phone; adb connect 192.168.0.161:<random port>; currently 192.168.0.161:43373 or via `adb mdns services`)

## Protocol/architecture

- Discovery: client broadcasts UDP 45310 `{"type":"discover"}` → host replies hello (name, capabilities)
- Control WS 45320: JSON messages — client sends `hello {name}` (device model) → `hello_ack`; `list_games` → games with base64 icons; `launch {game,width,height,fps,bitrate}` / `launch_desktop`; `stop`; `keyframe` → force IDR; `started` reply
- Video UDP 45330: 14-byte header (V/PT 1, seq 2, ts 4 @90kHz, frameId 1, nalType 1, fragId 2, fragCount 2, reserved 1) + H.264 NAL payload. **SPS+PPS merged into IDR NAL by VideoSender**, wire type forced to 5 (IDR) for merged NALs — critical fix
- Audio UDP 45340: 4-byte header + ADTS AAC
- Input UDP 45350: type byte (0=gamepad 40B, 1=keyboard, 2=mouse)
- Latency knobs: no B-frames, -tune ll, -force_key_frames every 2s, CBR, KEY_LOW_LATENCY decode, drop-stale

## Key fixes already applied (long session)

1. **Client crash on open**: NetworkOnMainThreadException in Discovery — moved to Dispatchers.IO
2. **Black screen #1**: decoder only queued IDR frames; SPS/PPS+IDR combined into one buffer
3. **Black screen #2**: 8-bit fragId/fragCount overflow on big keyframes → 16-bit header (14 bytes)
4. **Black screen #3**: per-NAL PTS bug → wire timestamp PTS
5. **Host console flash**: OutputType=WinExe
6. **Host crash on stream start**: DesktopCapture NRE (double-dispose bug in output enumeration) — fixed; stream thread fully try/catch guarded; session not disposed on WS EOF
7. **IDR never on wire**: NVENC -g unreliable with ll tune → added -force_key_frames expr; VideoSender merge forced wire type 5; keyframe repetition (send IDR twice); IDR pacing (~2.5 Mbps) for lossy 2.4GHz
8. **Client WS churn**: ControlChannelHolder singleton (survives activity recreation); ControlChannel send-queue until onOpen, reconnect on failure/closed, idempotent connect
9. **Games list slow**: host caches list_games 30s; client fetch timeout 3s→10s
10. **Keyframe on loss**: receiver requests keyframe on any seq gap / incomplete NAL; host honors via keyframe message
11. **Receiver socket**: 8MB receiveBufferSize
12. **Steam launch args removed** (yes/no popup); fps clamped to 60/30
13. **WS Unknown opcode: 0 (SOLVED)**: `BuildFrame` allocated `payload.Length + 10` for ALL frame sizes. Small (<126B) payloads left 8 zero bytes at the tail; 16-bit (126..65535B) payloads left 6 zero bytes at the tail. OkHttp read the trailing zeroes as a frame header with `opcode = 0` (continuation frame). Fixed `BuildFrame` to allocate exact size (+2, +4, or +10).
14. **Black screen / MediaCodec H.264 Annex-B start codes (SOLVED)**:
    - `H264Encoder.Drain()` was stripping `00 00 00 01` start codes, and `VideoSender` was concatenating SPS+PPS+IDR without delimiters, producing an unparseable buffer for Qualcomm MediaCodec.
    - Fixed `H264Encoder.Drain()` to keep Annex-B start codes intact.
    - Fixed `VideoSender` to send SPS+PPS+IDR with start codes intact as a self-contained Access Unit.
    - Fixed `VideoSender` to tag `nalType` on every packet fragment so packet loss doesn't erase NAL type metadata.
    - Removed artificial sleep/pacing in `VideoSender` that was stalling the video pipeline for ~2s per keyframe.
    - Fixed FFmpeg `-force_key_frames` expression from `expr:gte(t,n_forced*120)` to `expr:gte(t,n_forced*2)` (t is in seconds, not frames).
    - Fixed `VideoDecoder` to accept intact Annex-B NAL units directly into MediaCodec.
15. **Virtual Display Game Placement & Resolution (SOLVED)**:
    - `DisplayManager.SetVirtualDisplayResolution` updated with `CDS_UPDATEREGISTRY` (0x00000001) so resolution changes (e.g. 1920x1080@60Hz) commit to the virtual display (`DISPLAY5`).
    - `GameLauncher.MoveGameWindowToDisplay` upgraded to continuously search by full title, title keywords, and process name for up to 35 seconds.
    - Added `ShowWindow(hwnd, SW_RESTORE)` prior to `SetWindowPos` to ensure fullscreen/borderless game windows are restored and repositioned onto `DISPLAY5` coordinates (`X=-1920, Y=0`), and brought into focus with `SetForegroundWindow`.
    - Host captures `DISPLAY5` via `GdiDisplayCapture`, keeping the game rendering on the virtual monitor and invisible on the host's physical screen (`DISPLAY1`).

## CURRENT STATE — LAST DIAGNOSTIC FINDINGS (most critical)

- **Host stream path WORKS end-to-end locally** (raw WS client test): hello_ack OK, launch_desktop OK, IDRs sent on wire (IDR=4 in 6s), host survives, 2000+ video packets/5s
- **Phone receives video** (receiver logs `rx raw 1214B from 192.168.0.101`, NALs complete type=1 P-slices + type=6 SEI) — BUT **no IDR (type=5) NALs ever complete on the phone**, so decoder gate `gotFirstIdr` stays false → black screen
- **Phone WS to host fails intermittently**: "Could not reach 192.168.0.101" — **JUST DIAGNOSED**: OkHttp error `Unknown opcode: 0` (ProtocolException) at WebSocketReader.readMessageFrame — the host's first frame after handshake reads as opcode 0x0 to OkHttp. PC clients (raw + ClientWebSocket) work fine.
  - Host-side reply-frame logging added just before context ran out: `reply frame[N] first bytes: XX-...` in host-stream.log — was NOT yet verified on a phone attempt
- Host log shows recurring `ws loop break: EOF on header read` for phone connections when connect fails
- Host window sometimes doesn't show client connected (registration depends on hello arriving; broken WS = no hello)
- Phone is on 2.4GHz Wi-Fi "Tusher - WIFI (2.4 GHZ)", IP 192.168.0.161; host IP 192.168.0.101 (Ethernet); firewall: HandHeld.Host Allow Public (network is Public)

## Host diagnostics

- `D:\Projects\HandHeld\artifacts\host-stream.log` — StreamSession + WS log (File.AppendAllText; session start/end, capture, dispose, WS breaks, reply-frame dump)
- Client logs: `adb logcat -s HandHeld:*` (session flow, rx raw, NAL complete/incomplete, WS connect failure)

## Known outstanding issues (in priority order)

1. **Black screen on phone** — root cause chain: phone never gets a complete IDR NAL (2.4GHz loss? burst? or the WS `Unknown opcode: 0` breaking the stream session before IDR arrives)
2. **WS `Unknown opcode: 0`** — host's first reply frame mis-parsed by OkHttp (PC clients fine). Suspect: host sends a 0x00 byte somewhere (pong echo? the reply-frame write?), or a race. **Next step: check host-stream.log reply-frame dump after a phone attempt**
3. **Host window client list** — depends on hello; broken when WS fails
4. Game launches on physical display still (VDD is primary now but games may open elsewhere; MoveGameWindowToDisplay exists but untested end to end)
5. Host taskbar icon — was fixed (app.ico) but not visually confirmed by user after last builds

## Suggested next steps

1. Have user attempt phone connect; read `host-stream.log` for `reply frame` lines to see what the host actually sends as first frame (expect `81 ...`)
2. If reply is `81`, the 0x00 comes from elsewhere — dump ALL host→client writes (pong path too)
3. Fix the pong path: verify it's not sending 0x00 when pingPayload read fails (ReadExactAsync returns byte[0]? → pong[0]=0x8A pong[1]=0 is fine; but if payload read returns null/empty differently)
4. Once WS is stable, IDR delivery: consider reducing bitrate to 8-10 Mbps, smaller keyframes, or TCP+TCP-like reliability for video
5. After stream works: verify game-on-virtual-display (MoveGameWindowToDisplay), host window client list, VDD disable on stop

## Environment quirks

- Windows 10 Pro N — no Media Foundation (Mfplat.dll missing) → **FFmpeg NVENC instead of MF**
- GRADLE_USER_HOME/D:\Dev\gradle-home — Gradle downloads cached there
- adb wireless port changes; use `adb mdns services` to discover
- Host IP: 192.168.0.101; phone: 192.168.0.161
