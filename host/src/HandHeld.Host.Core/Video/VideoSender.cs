using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace HandHeld.Host.Core.Video;

/// <summary>Sends H.264 Annex-B access units as UDP datagrams with a small RTP-style header.</summary>
public sealed class VideoSender : IDisposable
{
    public const int MaxPayload = 1200;

    private readonly UdpClient _udp = new();
    private readonly IPEndPoint _remote;
    private ushort _sequence;
    private byte _frameId;
    private byte[]? _lastSps;
    private byte[]? _lastPps;

    public VideoSender(IPEndPoint remote) => _remote = remote;

    public void SendAccessUnit(byte[] au, long timestampMs, bool keyframe)
    {
        if (au.Length < 4) return;
        int scLen = (au.Length > 2 && au[2] == 1) ? 3 : 4;
        int nalType = (au.Length > scLen) ? (au[scLen] & 0x1F) : 1;

        if (nalType == 7) // SPS
        {
            _lastSps = au;
            return;
        }

        if (nalType == 8) // PPS
        {
            _lastPps = au;
            return;
        }

        if (nalType == 6) // SEI
        {
            // Optional: send SEI as standalone NAL
            SendNal(au, 6, timestampMs);
            return;
        }

        if (nalType == 5) // IDR
        {
            // Prepend SPS and PPS (with their start codes intact) to the IDR
            // so the client always receives a self-contained keyframe Access Unit.
            byte[] fullKeyframe = au;
            if (_lastSps != null && _lastPps != null)
            {
                fullKeyframe = Concat(Concat(_lastSps, _lastPps), au);
            }

            SendNal(fullKeyframe, 5, timestampMs);
            return;
        }

        // P-slice (or other slice)
        SendNal(au, nalType, timestampMs);
    }

    private void SendNal(byte[] au, int nalType, long timestampMs)
    {
        int fragCount = Math.Max(1, (au.Length + MaxPayload - 1) / MaxPayload);
        int offset = 0;
        int fragId = 0;

        while (offset < au.Length || fragId == 0)
        {
            int len = Math.Min(MaxPayload, au.Length - offset);
            if (len <= 0 && fragId > 0) break;

            // 14-byte header: V/PT(1) seq(2) ts(4) frameId(1) nalType(1) fragId(2) fragCount(2) res(1)
            var packet = new byte[14 + len];
            packet[0] = 0x10;                    // V=1, PT=0
            packet[1] = (byte)(_sequence >> 8);
            packet[2] = (byte)_sequence;
            _sequence++;
            long ts = timestampMs * 90;          // 90 kHz units
            packet[3] = (byte)(ts >> 24);
            packet[4] = (byte)(ts >> 16);
            packet[5] = (byte)(ts >> 8);
            packet[6] = (byte)ts;
            packet[7] = _frameId;
            packet[8] = (byte)nalType;           // NAL type on EVERY fragment so loss doesn't erase it
            packet[9] = (byte)(fragId >> 8);
            packet[10] = (byte)fragId;
            packet[11] = (byte)(fragCount >> 8);
            packet[12] = (byte)fragCount;
            packet[13] = 0;                      // reserved

            Array.Copy(au, offset, packet, 14, len);
            _udp.Send(packet, packet.Length, _remote);
            offset += len;
            fragId++;
        }

        _frameId++;
    }

    private static byte[] Concat(byte[] a, byte[] b)
    {
        var r = new byte[a.Length + b.Length];
        Buffer.BlockCopy(a, 0, r, 0, a.Length);
        Buffer.BlockCopy(b, 0, r, a.Length, b.Length);
        return r;
    }

    public void Dispose() => _udp.Dispose();
}
