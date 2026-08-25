package com.handheld.client.net

import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withTimeoutOrNull
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.Response
import okhttp3.WebSocket
import okhttp3.WebSocketListener
import org.json.JSONObject
import java.util.concurrent.TimeUnit

/**
 * WebSocket control channel to a host. JSON text frames both ways.
 * Wire format: protocols/control.md (same as host).
 */
class ControlChannel(
    val hostIp: String,
    private val scope: CoroutineScope,
    onMessage: (JSONObject) -> Unit,
) {
    private val client = OkHttpClient.Builder()
        .pingInterval(10, TimeUnit.SECONDS)
        .readTimeout(0, TimeUnit.MILLISECONDS)
        .build()

    private var webSocket: WebSocket? = null
    private var helloSent = false
    private val pending = java.util.concurrent.ConcurrentLinkedQueue<String>()
    var onMessage: (JSONObject) -> Unit = onMessage

    fun connect() {
        val existing = webSocket
        if (existing != null) return // already connecting/connected
        val request = Request.Builder().url("ws://$hostIp:45320/").build()
        webSocket = client.newWebSocket(request, object : WebSocketListener() {
            override fun onOpen(ws: WebSocket, response: Response) {
                // Handshake: announce device name so the host window shows it.
                helloSent = true
                val deviceName = android.os.Build.MODEL.ifBlank { "Android" }
                ws.send(JSONObject().put("type", "hello").put("name", deviceName).toString())
                // Flush anything queued while the socket was connecting.
                while (true) {
                    val msg = pending.poll() ?: break
                    ws.send(msg)
                }
            }

            override fun onMessage(ws: WebSocket, text: String) {
                try {
                    onMessage(JSONObject(text))
                } catch (_: Exception) {
                }
            }

            override fun onFailure(ws: WebSocket, t: Throwable, response: Response?) {
                android.util.Log.e("HandHeld", "WS connect failure: ${t.message} (response=${response?.code})", t)
                // Allow reconnects: clear the socket AND the hello flag so a
                // later connect() re-registers with the host.
                if (webSocket === ws) {
                    webSocket = null
                    helloSent = false
                }
            }

            override fun onClosed(ws: WebSocket, code: Int, reason: String) {
                if (webSocket === ws) {
                    webSocket = null
                    helloSent = false
                }
            }
        })
    }

    fun send(message: JSONObject) {
        val ws = webSocket
        if (ws != null && helloSent) {
            if (!ws.send(message.toString())) {
                // Send failed (socket dead) — reconnect and queue.
                webSocket = null
                helloSent = false
                pending.add(message.toString())
                connect()
            }
        } else if (ws == null) {
            // No socket — connect then queue.
            pending.add(message.toString())
            connect()
        } else {
            // Socket connecting — queue until onOpen.
            pending.add(message.toString())
        }
    }

    fun requestGames() {
        send(JSONObject().put("type", "list_games"))
    }

    fun requestKeyframe() {
        send(JSONObject().put("type", "keyframe"))
    }

    fun close() {
        webSocket?.close(1000, "bye")
        webSocket = null
    }
}
