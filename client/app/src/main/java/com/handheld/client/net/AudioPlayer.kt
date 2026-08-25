package com.handheld.client.net

import android.media.AudioAttributes
import android.media.AudioFormat
import android.media.AudioTrack
import android.media.MediaCodec
import android.media.MediaCodecInfo
import android.media.MediaFormat
import java.nio.ByteBuffer

/**
 * Receives ADTS AAC frames over UDP and plays them via MediaCodec decode
 * into a low-latency AudioTrack. 4-byte header (tag + 24-bit ms) + ADTS.
 */
class AudioPlayer(private val hostIp: String) {
    private val socket = java.net.DatagramSocket(45340)
    private val buf = ByteArray(4096)
    private var running = false
    private var thread: Thread? = null

    private var codec: MediaCodec? = null
    private var track: AudioTrack? = null
    private var ptsUs = 0L

    private fun ensureCodec() {
        if (codec != null) return
        val format = MediaFormat.createAudioFormat("audio/mp4a-latm", 48000, 2)
        format.setInteger(MediaFormat.KEY_AAC_PROFILE, MediaCodecInfo.CodecProfileLevel.AACObjectLC)
        format.setInteger(MediaFormat.KEY_BIT_RATE, 128000)
        format.setInteger(MediaFormat.KEY_MAX_INPUT_SIZE, 4096)
        codec = MediaCodec.createDecoderByType("audio/mp4a-latm").apply {
            configure(format, null, null, 0)
            start()
        }
        track = AudioTrack.Builder()
            .setAudioAttributes(
                AudioAttributes.Builder()
                    .setUsage(AudioAttributes.USAGE_GAME)
                    .setContentType(AudioAttributes.CONTENT_TYPE_MUSIC)
                    .build()
            )
            .setAudioFormat(
                AudioFormat.Builder()
                    .setEncoding(AudioFormat.ENCODING_PCM_16BIT)
                    .setSampleRate(48000)
                    .setChannelMask(AudioFormat.CHANNEL_OUT_STEREO)
                    .build()
            )
            .setTransferMode(AudioTrack.MODE_STREAM)
            .setPerformanceMode(AudioTrack.PERFORMANCE_MODE_LOW_LATENCY)
            .build()
        track!!.play()
    }

    fun start() {
        running = true
        thread = Thread {
            while (running) {
                try {
                    val packet = java.net.DatagramPacket(buf, buf.size)
                    socket.receive(packet)
                    val n = packet.length
                    if (n < 8) continue
                    // Skip 4-byte header, rest is one ADTS frame.
                    val frame = ByteArray(n - 4)
                    System.arraycopy(buf, 4, frame, 0, n - 4)
                    decode(frame)
                } catch (_: Exception) {
                }
            }
        }
        thread!!.start()
    }

    private fun decode(frame: ByteArray) {
        ensureCodec()
        val c = codec ?: return
        val inIdx = c.dequeueInputBuffer(10_000)
        if (inIdx >= 0) {
            val inBuf = c.getInputBuffer(inIdx)!!
            inBuf.clear()
            inBuf.put(frame)
            c.queueInputBuffer(inIdx, 0, frame.size, ptsUs, 0)
            ptsUs += 21_333 // ~46.9 fps ADTS (1024 samples @ 48k)
        }
        // Drain decoded PCM to AudioTrack.
        while (true) {
            val info = MediaCodec.BufferInfo()
            val outIdx = c.dequeueOutputBuffer(info, 0)
            if (outIdx == MediaCodec.INFO_TRY_AGAIN_LATER) break
            if (outIdx >= 0) {
                val outBuf = c.getOutputBuffer(outIdx)!!
                val pcm = ByteArray(info.size)
                outBuf.get(pcm)
                track?.write(pcm, 0, pcm.size)
                c.releaseOutputBuffer(outIdx, false)
            }
        }
    }

    fun stop() {
        running = false
        thread?.join(2000)
        socket.close()
        try { codec?.stop() } catch (_: Exception) {}
        codec?.release()
        track?.stop()
        track?.release()
        codec = null
        track = null
    }
}
