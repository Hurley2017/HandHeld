package com.handheld.client.net

import android.util.Log
import android.view.Surface
import kotlinx.coroutines.CoroutineScope
import org.json.JSONObject

/**
 * Owns one streaming session: control WS + video receiver + decoder.
 * Connects, requests the chosen game (or desktop) stream, feeds decoded video
 * to the Surface.
 */
class SessionManager(
    private val hostIp: String,
    private val scope: CoroutineScope,
    private val providedControl: ControlChannel? = null,
) {
    var onStatus: ((String) -> Unit)? = null

    private var control: ControlChannel? = providedControl
    private var receiver: VideoReceiver? = null
    private var decoder: VideoDecoder? = null
    private var audio: AudioPlayer? = null
    private var surface: Surface? = null
    private var started = false
    private var awaitingSurfaceForStream = false
    private var pendingGame: String? = null

    /** gameId = "desktop" or a game id from the host's game list. */
    fun start(gameId: String) {
        Log.i("HandHeld", "session.start($gameId) surface=${surface != null}")
        if (started || surface == null) {
            pendingGame = gameId
            return
        }
        doStart(gameId)
    }

    fun onSurfaceReady(surface: Surface) {
        Log.i("HandHeld", "session.onSurfaceReady")
        this.surface = surface
        pendingGame?.let { doStart(it) }
        if (awaitingSurfaceForStream) {
            awaitingSurfaceForStream = false
            Log.i("HandHeld", "retrying pipeline now that surface exists")
            startPipeline()
        }
    }

    private fun startPipeline() {
        val s = surface ?: return
        val w = 1920
        val h = 1080
        Log.i("HandHeld", "starting video/audio pipeline (${w}x$h)")
        try {
            decoder = VideoDecoder(w, h, s)
            receiver = VideoReceiver(hostIp, decoder!!).also {
                it.onKeyframeRequested = { control?.requestKeyframe() }
                it.start()
            }
            audio = AudioPlayer(hostIp).also { it.start() }
        } catch (e: Exception) {
            Log.e("HandHeld", "pipeline start failed: $e")
        }
    }

    fun onSurfaceDestroyed() {
        Log.i("HandHeld", "session.onSurfaceDestroyed")
        surface = null
        dispose()
    }

    private fun doStart(gameId: String) {
        Log.i("HandHeld", "doStart($gameId)")
        if (started) return
        started = true
        pendingGame = null
        onStatus?.invoke("Connecting…")

        val ch = control
        if (ch == null) {
            // No persistent channel (e.g. desktop from a fresh host) — open one.
            control = ControlChannel(hostIp, scope) { msg ->
                Log.i("HandHeld", "WS msg: ${msg.optString("type")}")
                handleControl(msg)
            }
            control?.connect()
        } else {
            // Reuse the persistent channel: attach our message handler.
            val prev = ch.onMessage
            ch.onMessage = { msg ->
                prev(msg)
                Log.i("HandHeld", "WS msg: ${msg.optString("type")}")
                handleControl(msg)
            }
            ch.connect() // idempotent — ensures the socket is open before launch
        }

        // Client's resolution drives the virtual display on the host (second display).
        // Values are set by the UI via setClientResolution() before start().
        val launch = JSONObject()
            .put("width", clientWidth)
            .put("height", clientHeight)
            .put("fps", 60)
            .put("bitrate", 20000)
        if (gameId == "desktop") {
            launch.put("type", "launch_desktop")
        } else {
            launch.put("type", "launch")
            launch.put("game", gameId)
        }
        control?.send(launch)
    }

    /** Set by the UI (has a Context) — the phone's real screen size in landscape. */
    var clientWidth: Int = 1920
    var clientHeight: Int = 1080

    private fun handleControl(msg: JSONObject) {
        val type = msg.optString("type")
        when (type) {
            "started" -> {
                onStatus?.invoke("Streaming")
                val s = surface
                if (s == null) {
                    Log.w("HandHeld", "started but no surface yet — retrying")
                    awaitingSurfaceForStream = true
                    return
                }
                startPipeline()
            }
            "error" -> {
                onStatus?.invoke(msg.optString("message", "Host error"))
                dispose()
            }
        }
    }

    fun dispose() {
        Log.i("HandHeld", "session.dispose")
        started = false
        awaitingSurfaceForStream = false
        receiver?.stop()
        decoder?.stop()
        audio?.stop()
        if (control != null && control !== providedControl) {
            control?.close()
        }
        receiver = null
        decoder = null
        audio = null
        control = null
    }
}
