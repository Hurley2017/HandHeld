# HandHeld Protocol v1 (LAN)

All multi-byte integers big-endian. JSON over WebSocket for control; custom UDP frames for media/input.

## Ports

| Port | Transport | Role |
|---|---|---|
| 45310 | UDP | Discovery (broadcast → unicast reply) |
| 45320 | TCP / WebSocket | Control (JSON messages) |
| 45330 | UDP | Video (H.264 NALs, RTP-style header) |
| 45340 | UDP | Audio (AAC ADTS) |
| 45350 | UDP | Input (client → host, 120 Hz) |

## Discovery (UDP 45310)

Client broadcasts JSON to 255.255.255.255:45310:

```json
{"type":"discover","app":"HandHeld","api":1}
```

Host replies (unicast to sender):

```json
{"type":"hello","app":"HandHeld","api":1,"host":"DESKTOP-RTX","capabilities":{"video":["h264","hevc"],"maxWidth":3840,"maxHeight":2160,"maxFps":120,"audio":["aac"],"gamepad":true}}
```

## Control (WebSocket 45320)

JSON text frames:

```json
{"type":"list_games"}
{"type":"games","games":[{"id":"steam_730","title":"Counter-Strike 2","source":"steam","image":"http://host:45320/img/steam_730.png"}]}
{"type":"launch","game":"steam_730","width":2560,"height":1440,"fps":60,"codec":"h264","bitrate":30000}
{"type":"launch_desktop","width":2560,"height":1440,"fps":60,"codec":"h264","bitrate":30000}
{"type":"started","width":2560,"height":1440,"fps":60,"codec":"h264"}
{"type":"stats","fps":59.8,"bitrate":28500,"captureMs":3.1,"encodeMs":2.2,"rttMs":4}
{"type":"keyframe"}
{"type":"stop"}
{"type":"error","message":"..."}
```

## Video (UDP 45330)

Per-packet header (12 bytes) + H.264 NAL payload:

```
0                   1                   2                   3
0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
| V=1 |PT=0 |F|R|     Seq      |           Timestamp            |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|   FrameId    |  NalType  |    FragId     |   FragCount   |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
```

- Seq: packet sequence (loss detection / NACK).
- Timestamp: ms host wall clock (A/V sync; 90 kHz units).
- FrameId: increment per frame; NalType = 1 (non-IDR) / 5 (IDR).
- FragId/FragCount: fragmentation for large NALs (> ~1200 B payload).
- NACK: client sends `{"type":"nack","seq":1234}` over WS → host sends IDR.

## Audio (UDP 45340)

ADTS AAC (44.1/48 kHz, stereo). Header (4 bytes) + ADTS payload:

```
0 1 2 3
+-+-+-+-+
|  Tag   |   Timestamp (24-bit ms)
+-+-+-+-+
```

- Tag 0 = audio, subsequent ADTS frames are one Access Unit each.

## Input (UDP 45350)

120 Hz snapshot. Header (4 bytes):

```
0 1 2 3
+-+-+-+-+
| Type  |  Device  |   Size    |
+-+-+-+-+
```

- Type 0 = Gamepad (Xbox-style), Device 0 = virtual pad, Size = 40:

```
bytes 0-1: buttons bitmask (16 bits: A,B,X,Y,LB,RB,LS,RS,Start,Back,Guide,DPadU,D,L,R)
bytes 2-3: left stick X (int16)
bytes 4-5: left stick Y (int16)
bytes 6-7: right stick X (int16)
bytes 8-9: right stick Y (int16)
bytes 10-11: left trigger (uint8)
bytes 11-12: right trigger (uint8)
remaining: reserved
```

- Type 1 = Keyboard: UTF-8 key code (Windows VK), modifiers byte.
- Type 2 = Mouse: bytes: buttons, dx (int16), dy (int16), wheel (int8), + reserved.

## Pairing (later, M6)

Optional 4-digit PIN: host config toggle. Handshake: client sends `{"type":"pair","pin":"1234"}` → `{"ok":true}` / `{"ok":false}`. v1 defaults open on LAN.
