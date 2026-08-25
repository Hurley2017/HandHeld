package com.handheld.client.net

import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.InetSocketAddress

object Discovery {
    const val PORT = 45310

    data class HostInfo(val name: String, val ip: String)

    /** Broadcast a discover message, collect unicast replies for [timeoutMs]. */
    fun discover(timeoutMs: Int = 2000): List<HostInfo> {
        val results = mutableListOf<HostInfo>()
        val socket = DatagramSocket()
        try {
            socket.soTimeout = timeoutMs
            val payload = "{\"type\":\"discover\",\"app\":\"HandHeld\",\"api\":1}".toByteArray()
            val broadcast = InetAddress.getByName("255.255.255.255")
            socket.send(DatagramPacket(payload, payload.size, broadcast, PORT))

            val buf = ByteArray(4096)
            val deadline = System.currentTimeMillis() + timeoutMs
            while (System.currentTimeMillis() < deadline) {
                val packet = DatagramPacket(buf, buf.size)
                try {
                    socket.receive(packet)
                } catch (_: java.net.SocketTimeoutException) {
                    break
                }
                val text = String(packet.data, 0, packet.length)
                if (text.contains("\"hello\"")) {
                    val ip = packet.address.hostAddress ?: continue
                    if (results.none { it.ip == ip }) {
                        val name = Regex("\"host\"\\s*:\\s*\"([^\"]+)\"")
                            .find(text)?.groupValues?.get(1) ?: ip
                        results.add(HostInfo(name, ip))
                    }
                }
            }
        } finally {
            socket.close()
        }
        return results
    }
}
