package com.handheld.client.net

import android.os.Handler
import android.os.Looper
import java.net.DatagramPacket
import java.net.DatagramSocket

/**
 * Receives H.264 UDP packets (14-byte header + NAL payload) from the host.
 * Each NAL (SPS/PPS/slice) is fragmented across `fragCount` datagrams
 * (16-bit fragId/fragCount); fragments of the current NAL are reassembled
 * in order and the complete NAL is handed to the decoder.
 */
class VideoReceiver(
    private val hostIp: String,
    private val decoder: VideoDecoder,
) {
    /** Invoked when the receiver detects loss and wants a fresh IDR from the host. */
    var onKeyframeRequested: (() -> Unit)? = null

    private val socket = DatagramSocket(45330).apply {
        // Big receive buffer — IDR bursts at 20Mbps are megabytes; the default
        // 64KB overflows and the OS drops fragments, killing keyframes.
        receiveBufferSize = 8 * 1024 * 1024
    }
    private val buf = ByteArray(64 * 1024)
    private var running = false
    private var thread: Thread? = null
    private var lastSeq = -1
    private var lostPackets = 0
    private var packetCount = 0
    private var lastLogMs = 0L
    private val handler = Handler(Looper.getMainLooper())

    // Current NAL being reassembled.
    private var curFrame = -1
    private var curNalType = 0
    private var curFragCount = 0
    private var curParts: Array<ByteArray?>? = null
    private var curReceived = 0

    fun start() {
        running = true
        thread = Thread {
            android.util.Log.i("HandHeld", "receiver thread started, bound 45330")
            while (running) {
                try {
                    val packet = DatagramPacket(buf, buf.size)
                    socket.receive(packet)
                    val len = packet.length
                    if (len < 14) continue

                    val frameId = buf[7].toInt() and 0xFF
                    val nalType = buf[8].toInt() and 0xFF
                    val fragId = ((buf[9].toInt() and 0xFF) shl 8) or (buf[10].toInt() and 0xFF)
                    val fragCount = ((buf[11].toInt() and 0xFF) shl 8) or (buf[12].toInt() and 0xFF)
                    val ts90k = ((buf[3].toLong() and 0xFF) shl 24) or ((buf[4].toLong() and 0xFF) shl 16) or
                        ((buf[5].toLong() and 0xFF) shl 8) or (buf[6].toLong() and 0xFF)
                    val ptsUs = ts90k * 1000L / 90L   // 90kHz → microseconds
                    val seq = ((buf[1].toInt() and 0xFF) shl 8) or (buf[2].toInt() and 0xFF)
                    packetCount++

                    val now = System.currentTimeMillis()
                    if (now - lastLogMs > 2000) {
                        android.util.Log.i("HandHeld", "rx stats: pkts=$packetCount lastNal=$nalType frag=$fragId/$fragCount frame=$frameId")
                        lastLogMs = now
                        packetCount = 0
                    }

                    if (lastSeq >= 0 && seq != lastSeq + 1) {
                        lostPackets++
                        if (lostPackets > 2) {
                            lostPackets = 0
                            handler.post {
                                decoder.requestKeyframe()
                                onKeyframeRequested?.invoke()
                            }
                        }
                    }
                    lastSeq = seq

                    // New NAL (frame change): start a fresh buffer.
                    if (frameId != curFrame) {
                        if (curParts != null && curReceived > 0 && curReceived < curFragCount) {
                            android.util.Log.w("HandHeld", "NAL incomplete: got $curReceived/$curFragCount (type=$curNalType), dropping")
                        }
                        curFrame = frameId
                        curNalType = nalType
                        curFragCount = if (fragCount > 0) fragCount else 1
                        curParts = arrayOfNulls(curFragCount)
                        curReceived = 0
                    }

                    val parts = curParts ?: continue
                    if (fragId < parts.size && parts[fragId] == null) {
                        val part = ByteArray(len - 14)
                        System.arraycopy(packet.data, 14, part, 0, part.size)
                        parts[fragId] = part
                        curReceived++
                    }

                    if (curReceived == parts.size) {
                        var total = 0
                        for (p in parts) total += p!!.size
                        val au = ByteArray(total)
                        var pos = 0
                        for (p in parts) {
                            System.arraycopy(p!!, 0, au, pos, p.size)
                            pos += p.size
                        }
                        val nalToFeed = curNalType
                        curParts = null
                        curReceived = 0
                        android.util.Log.i("HandHeld", "NAL complete: type=$nalToFeed size=$total frags=${parts.size}")
                        decoder.feed(au, nalToFeed, ptsUs)
                    }
                } catch (_: Exception) {
                }
            }
        }
        thread!!.start()
    }

    fun stop() {
        running = false
        thread?.join(2000)
        socket.close()
    }
}
