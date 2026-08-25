package com.handheld.client.net

import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import org.json.JSONObject

/**
 * Process-wide holder for the control channel to the selected host.
 * Survives activity recreation (rotation, background) so the WS connection
 * and the host-side client registration are not lost.
 */
object ControlChannelHolder {
    private var channel: ControlChannel? = null

    /** Returns the existing channel for [hostIp] or creates one. */
    fun get(hostIp: String): ControlChannel {
        channel?.let { if (it.hostIp == hostIp) return it }
        channel?.close()
        val c = ControlChannel(hostIp, CoroutineScope(Dispatchers.IO)) { }
        channel = c
        return c
    }

    fun current(): ControlChannel? = channel

    fun close() {
        channel?.close()
        channel = null
    }
}
