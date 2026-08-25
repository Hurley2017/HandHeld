package com.handheld.client.net

import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress

/**
 * Sends gamepad / keyboard / mouse input to the host over UDP at ~120 Hz.
 * Wire format matches the host InputReceiver (see docs/protocol.md).
 */
class InputSender(private val hostIp: String) {
    private val socket = DatagramSocket()
    private val address: InetAddress = InetAddress.getByName(hostIp)

    // ---- Gamepad state ---------------------------------------------------
    var buttons: Int = 0
        private set
    var leftX: Short = 0
        private set
    var leftY: Short = 0
        private set
    var rightX: Short = 0
        private set
    var rightY: Short = 0
        private set
    var leftTrigger: Byte = 0
        private set
    var rightTrigger: Byte = 0
        private set

    fun setButton(bit: Int, pressed: Boolean) {
        buttons = if (pressed) buttons or bit else buttons and bit.inv()
    }

    fun setStick(left: Boolean, x: Float, y: Float) {
        val sx = (x.coerceIn(-1f, 1f) * 32767f).toInt().toShort()
        val sy = (y.coerceIn(-1f, 1f) * 32767f).toInt().toShort()
        if (left) { leftX = sx; leftY = sy } else { rightX = sx; rightY = sy }
    }

    fun setTrigger(left: Boolean, value: Float) {
        val v = (value.coerceIn(0f, 1f) * 255f).toInt().toByte()
        if (left) leftTrigger = v else rightTrigger = v
    }

    // ---- Send ------------------------------------------------------------
    fun sendGamepad() {
        val data = ByteArray(40)
        data[0] = 0                      // type: gamepad
        data[1] = 0                      // device
        data[2] = 40                     // size
        data[3] = ((buttons shr 8) and 0xFF).toByte()
        data[4] = (buttons and 0xFF).toByte()
        data[5] = (leftX.toInt() shr 8).toByte()
        data[6] = (leftX.toInt() and 0xFF).toByte()
        data[7] = (leftY.toInt() shr 8).toByte()
        data[8] = (leftY.toInt() and 0xFF).toByte()
        data[9] = (rightX.toInt() shr 8).toByte()
        data[10] = (rightX.toInt() and 0xFF).toByte()
        data[11] = (rightY.toInt() shr 8).toByte()
        data[12] = (rightY.toInt() and 0xFF).toByte()
        data[13] = leftTrigger
        data[14] = rightTrigger
        socket.send(DatagramPacket(data, data.size, address, 45350))
    }

    /** Keyboard: vk = Windows virtual key code, down = pressed. */
    fun sendKey(vk: Byte, down: Boolean) {
        val data = ByteArray(7)
        data[0] = 1                      // type: keyboard
        data[1] = 0
        data[2] = 7
        data[3] = 0                      // modifiers
        data[4] = vk
        data[5] = if (down) 1 else 0
        socket.send(DatagramPacket(data, data.size, address, 45350))
    }

    /** Mouse: relative move + buttons bitmask (1=left,2=right,4=middle) + wheel. */
    fun sendMouse(dx: Short, dy: Short, buttons: Int, wheel: Byte) {
        val data = ByteArray(10)
        data[0] = 2                      // type: mouse
        data[1] = 0
        data[2] = 10
        data[3] = buttons.toByte()
        data[4] = (dx.toInt() shr 8).toByte()
        data[5] = (dx.toInt() and 0xFF).toByte()
        data[6] = (dy.toInt() shr 8).toByte()
        data[7] = (dy.toInt() and 0xFF).toByte()
        data[8] = wheel
        socket.send(DatagramPacket(data, data.size, address, 45350))
    }

    fun close() = socket.close()
}
