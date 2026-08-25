# HandHeld — Architecture

Local-network game streaming: Windows host (RTX 5060) → Samsung S24 (Android 16).

## Components

| Side | Tech | Role |
|---|---|---|
| Host | C# .NET 8 WPF | Tray app: discovery responder, capture/encode, input injection, launcher |
| Client | Kotlin + Jetpack Compose | Discovery list, launcher grid, streaming session (MediaCodec decode) |

## Host pipeline

```
DXGI Desktop Duplication ──► NVENC H.264 (Media Foundation) ──► UDP 45330
WASAPI loopback ──────────► AAC (MF) ────────────────────────► UDP 45340
UDP 45350 (input) ────────► SendInput (kbd/mouse) + ViGEmBus (X360 pad)
WebSocket 45320 ─────────► control (games list, launch, settings, stats)
UDP 45310 broadcast ──────► discovery reply
```

## Client pipeline

```
UDP 45310 discovery ─► host list
WS 45320 ────────────► control
UDP 45330 ───────────► MediaCodec H.264 ─► SurfaceView (drop-stale-frames policy)
UDP 45340 ───────────► MediaCodec AAC ─► AudioTrack (low-latency mode)
Gamepad / touch ─────► UDP 45350 (120 Hz)
```

## Session flow

1. Client broadcasts discovery → host replies (name, capabilities, API version).
2. Client opens WebSocket control channel, requests game list.
3. User picks game (or "Desktop") → host launches (borderless fullscreen) or attaches to desktop.
4. Host starts video/audio streams; client decodes; input flows back over UDP.
5. Stats (fps, bitrate, decode ms, round-trip) flow over WS for the overlay.

## Latency knobs

- No B-frames, short GOP, CBR (NVENC low-latency preset).
- MediaCodec `KEY_LOW_LATENCY`, `SurfaceView` rendering, `AudioTrack` PERFORMANCE_MODE_LOW_LATENCY.
- Client drops stale video frames (audio is master); NACK on loss → host sends IDR.
