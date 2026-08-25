package com.handheld.client.net

import android.media.MediaCodec
import android.media.MediaCodecInfo
import android.media.MediaFormat
import android.view.Surface

/**
 * Decodes H.264 Annex-B NALs with hardware MediaCodec into the given Surface.
 * Keyframe Access Units contain [SPS][PPS][IDR] with Annex-B start codes.
 * PTS comes from the wire timestamp (per-frame), so playback pacing is real.
 */
class VideoDecoder(
    private val width: Int,
    private val height: Int,
    private val surface: Surface,
) {
    private val codec: MediaCodec = MediaCodec.createDecoderByType("video/avc")
    private var started = false
    private var gotFirstIdr = false
    private var basePtsUs = 0L
    private var lastPtsUs = 0L
    private var renderCount = 0

    init {
        val format = MediaFormat.createVideoFormat("video/avc", width, height)
        format.setInteger(MediaFormat.KEY_COLOR_FORMAT, MediaCodecInfo.CodecCapabilities.COLOR_FormatSurface)
        format.setInteger(MediaFormat.KEY_LOW_LATENCY, 1)
        codec.configure(format, surface, null, 0)
        codec.start()
        started = true
        android.util.Log.i("HandHeld", "VideoDecoder started: ${width}x${height}")
    }

    /** Feed one complete H.264 Annex-B NAL. [wireNalType] 7/8/5/1/6; [ptsUs] per-frame from the wire. */
    fun feed(nal: ByteArray, wireNalType: Int, ptsUs: Long) {
        if (nal.size < 4) return
        val nalType = wireNalType and 0x1F

        // Ensure Annex-B start code is present
        val buffer = if (hasStartCode(nal)) {
            nal
        } else {
            val annb = ByteArray(nal.size + 4)
            annb[0] = 0; annb[1] = 0; annb[2] = 0; annb[3] = 1
            System.arraycopy(nal, 0, annb, 4, nal.size)
            annb
        }

        // Normalize PTS: anchor the first frame at 0, monotonic from there.
        if (basePtsUs == 0L) basePtsUs = ptsUs
        var pts = ptsUs - basePtsUs
        if (pts < lastPtsUs) pts = lastPtsUs + 16_667
        lastPtsUs = pts

        when (nalType) {
            5 -> {
                // IDR keyframe (includes SPS+PPS)
                gotFirstIdr = true
                queueInput(buffer, pts, 0)
                android.util.Log.i("HandHeld", "dec: IDR queued (${buffer.size}B pts=$pts)")
            }
            1 -> {
                // P-slice
                if (gotFirstIdr) {
                    queueInput(buffer, pts, 0)
                }
            }
            7, 8, 6 -> {
                // SPS, PPS, or SEI
                queueInput(buffer, pts, 0)
            }
        }
        drain()
    }

    private fun hasStartCode(data: ByteArray): Boolean {
        if (data.size < 4) return false
        if (data[0].toInt() == 0 && data[1].toInt() == 0 && data[2].toInt() == 1) return true
        if (data[0].toInt() == 0 && data[1].toInt() == 0 && data[2].toInt() == 0 && data[3].toInt() == 1) return true
        return false
    }

    private fun queueInput(data: ByteArray, ptsUs: Long, flags: Int) {
        val idx = codec.dequeueInputBuffer(10_000)
        if (idx >= 0) {
            val buf = codec.getInputBuffer(idx)!!
            buf.clear()
            buf.put(data)
            codec.queueInputBuffer(idx, 0, data.size, ptsUs, flags)
        }
    }

    private fun drain() {
        while (true) {
            val info = MediaCodec.BufferInfo()
            val outIdx = codec.dequeueOutputBuffer(info, 0)
            if (outIdx == MediaCodec.INFO_TRY_AGAIN_LATER) break
            if (outIdx >= 0) {
                codec.releaseOutputBuffer(outIdx, true)
                renderCount++
                if (renderCount <= 5 || renderCount % 120 == 0) {
                    android.util.Log.i("HandHeld", "dec: rendered frame #$renderCount (${info.size}B)")
                }
            }
        }
    }

    fun requestKeyframe() {
        gotFirstIdr = false
        basePtsUs = 0L
        lastPtsUs = 0L
    }

    fun stop() {
        if (started) {
            try { codec.stop() } catch (_: Exception) {}
            codec.release()
            started = false
        }
    }
}
