# HandHeld

Local-network game streaming: **Windows PC (host)** → **Android phone (client)**. Own Moonlight/Sunshine replacement.

## Status

- ✅ M0 Toolchain (all on `D:`)
- ✅ M1 Discovery + control (UDP broadcast + WebSocket)
- ✅ M2 Video: DXGI capture → NVENC H.264 → MediaCodec decode (1440p60 capable)
- ✅ M3 Input: gamepad (EvoFox Deck 2 HID) + keyboard/mouse
- ✅ M4 Audio: WASAPI loopback → AAC → AudioTrack
- ✅ M5 Launcher: Steam scan + shortcut scan, game grid on phone
- 🔄 M6 Polish: install script, stats overlay, docs

## Requirements

- Host: Windows 10/11 x64, NVIDIA GPU (NVENC), ~200 MB free on D:
- Client: Android 11+ (S24 on One UI 8.5 works), APK sideloading enabled
- Both on the same LAN

## Setup (one time)

```powershell
# 1. Install toolchains (all to D:\Dev, nothing on C:)
powershell -ExecutionPolicy Bypass -File scripts\setup-all.ps1

# 2. Build everything
powershell -ExecutionPolicy Bypass -File scripts\build-all.ps1
```

## Run

```powershell
# Host (tray app, discovery + streaming)
.\scripts\run-host.ps1

# Phone: copy artifacts\HandHeld-debug.apk to the phone and install,
# or use ADB:
.\scripts\install-apk.ps1            # USB
.\scripts\install-apk.ps1 -Wireless  # ADB over Wi-Fi
```

On the phone: open HandHeld → pick your PC → pick a game (or Desktop) → play.
Connect the EvoFox Deck 2 in Android HID mode (hold **DOJO + A**) first.

## Phone setup notes (S24 / One UI 8.5)

1. Settings → Security and privacy → **Auto Blocker OFF** (or allow the installer app).
2. Settings → Apps → the browser/file manager you use → **Install unknown apps → Allow**.
3. First install shows a Play Protect warning → "Install anyway".

## Notes

- **ViGEmBus driver**: needed for the virtual Xbox 360 gamepad on the host
  (one-time install, admin). Keyboard/mouse input works without it.
- **Windows N editions**: Media Foundation may be missing; the host uses
  FFmpeg (NVENC) directly, so it works without the Media Feature Pack.
- Everything (SDKs, caches, builds) lives on `D:\Dev` / `D:\Projects\HandHeld\artifacts`.

## Docs

- `docs/architecture.md` — component diagram, pipelines
- `docs/protocol.md` — wire protocol (ports, message formats)
