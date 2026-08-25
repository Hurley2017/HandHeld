package com.handheld.client.ui

import android.content.Context
import android.view.InputDevice
import android.view.KeyEvent
import android.view.MotionEvent
import android.view.SurfaceHolder
import android.view.SurfaceView
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import com.handheld.client.net.Discovery.HostInfo
import com.handheld.client.net.InputSender
import com.handheld.client.net.SessionManager
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob

/** Full-screen streaming session: video surface + gamepad input + exit overlay. */
@Composable
fun StreamScreen(host: HostInfo, gameId: String, channel: com.handheld.client.net.ControlChannel?, onExit: () -> Unit) {
    val scope = remember { CoroutineScope(SupervisorJob() + Dispatchers.Default) }
    val session = remember { SessionManager(host.ip, scope, channel) }
    val input = remember { InputSender(host.ip) }
    var status by remember { mutableStateOf("Starting…") }
    val context = LocalContext.current

    LaunchedEffect(Unit) {
        session.onStatus = { status = it }
        // Phone's real screen size (landscape) drives the host's virtual display.
        val dm = context.resources.displayMetrics
        session.clientWidth = maxOf(dm.widthPixels, dm.heightPixels)
        session.clientHeight = minOf(dm.widthPixels, dm.heightPixels)
        session.start(gameId)
    }

    Box(Modifier.fillMaxSize()) {
        AndroidView(
            factory = { ctx: Context ->
                SurfaceView(ctx).apply {
                    holder.addCallback(object : SurfaceHolder.Callback {
                        override fun surfaceCreated(holder: SurfaceHolder) {
                            session.onSurfaceReady(holder.surface)
                        }
                        override fun surfaceChanged(holder: SurfaceHolder, format: Int, w: Int, h: Int) {}
                        override fun surfaceDestroyed(holder: SurfaceHolder) {
                            session.onSurfaceDestroyed()
                        }
                    })
                    // Physical gamepad (EvoFox Deck 2 in HID mode).
                    setOnGenericMotionListener { _, event ->
                        if (event.source and InputDevice.SOURCE_JOYSTICK == InputDevice.SOURCE_JOYSTICK) {
                            val lx = event.getAxisValue(MotionEvent.AXIS_X)
                            val ly = event.getAxisValue(MotionEvent.AXIS_Y)
                            val rx = event.getAxisValue(MotionEvent.AXIS_Z)
                            val ry = event.getAxisValue(MotionEvent.AXIS_RZ)
                            val lt = event.getAxisValue(MotionEvent.AXIS_LTRIGGER)
                            val rt = event.getAxisValue(MotionEvent.AXIS_RTRIGGER)
                            input.setStick(true, lx, ly)
                            input.setStick(false, rx, ry)
                            input.setTrigger(true, lt)
                            input.setTrigger(false, rt)
                            input.sendGamepad()
                        }
                        true
                    }
                    setOnKeyListener { _, keyCode, event ->
                        val bit = when (keyCode) {
                            KeyEvent.KEYCODE_BUTTON_A, KeyEvent.KEYCODE_DPAD_CENTER -> 0x0001
                            KeyEvent.KEYCODE_BUTTON_B -> 0x0002
                            KeyEvent.KEYCODE_BUTTON_X -> 0x0004
                            KeyEvent.KEYCODE_BUTTON_Y -> 0x0008
                            KeyEvent.KEYCODE_BUTTON_L1 -> 0x0010
                            KeyEvent.KEYCODE_BUTTON_R1 -> 0x0020
                            KeyEvent.KEYCODE_BUTTON_THUMBL -> 0x0040
                            KeyEvent.KEYCODE_BUTTON_THUMBR -> 0x0080
                            KeyEvent.KEYCODE_BUTTON_START -> 0x0100
                            KeyEvent.KEYCODE_BUTTON_SELECT -> 0x0200
                            KeyEvent.KEYCODE_BUTTON_MODE -> 0x0400
                            KeyEvent.KEYCODE_DPAD_UP -> 0x0800
                            KeyEvent.KEYCODE_DPAD_DOWN -> 0x1000
                            KeyEvent.KEYCODE_DPAD_LEFT -> 0x2000
                            KeyEvent.KEYCODE_DPAD_RIGHT -> 0x4000
                            else -> 0
                        }
                        if (bit != 0) {
                            input.setButton(bit, event.action != KeyEvent.ACTION_UP)
                            input.sendGamepad()
                        }
                        true
                    }
                }
            },
            modifier = Modifier.fillMaxSize()
        )

        // Hidden control overlay: tap anywhere to reveal it centered.
        var overlayVisible by remember { mutableStateOf(false) }
        if (overlayVisible) {
            Box(
                Modifier
                    .fillMaxSize()
                    .background(Color(0x88000000))
                    .clickable(indication = null, interactionSource = null) { overlayVisible = false }
            ) {
                Column(
                    Modifier.align(Alignment.Center),
                    horizontalAlignment = Alignment.CenterHorizontally
                ) {
                    Text(
                        "HandHeld",
                        style = MaterialTheme.typography.titleLarge,
                        color = Color.White
                    )
                    Text(
                        status,
                        style = MaterialTheme.typography.bodyMedium,
                        color = Color(0xFFB0BEC5),
                        modifier = Modifier.padding(bottom = 24.dp)
                    )
                    Button(onClick = onExit) {
                        Text("Exit Stream")
                    }
                    Text(
                        "Tap anywhere to dismiss",
                        style = MaterialTheme.typography.bodySmall,
                        color = Color(0xFF90A4AE),
                        modifier = Modifier.padding(top = 12.dp)
                    )
                }
            }
        } else {
            // Invisible full-screen tap catcher → reveal overlay.
            Box(
                Modifier
                    .fillMaxSize()
                    .clickable(indication = null, interactionSource = null) { overlayVisible = true }
            )
        }
    }

    DisposableEffect(Unit) {
        onDispose {
            session.dispose()
            input.close()
        }
    }
}
