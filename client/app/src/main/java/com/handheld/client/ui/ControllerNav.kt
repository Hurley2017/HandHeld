package com.handheld.client.ui

import android.view.KeyEvent
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.input.key.key
import androidx.compose.ui.input.key.onPreviewKeyEvent
import androidx.compose.ui.input.key.Key

/**
 * Handles a physical controller's D-pad + A/B (and keyboard arrows + Enter/Esc)
 * to move a highlighted selection through a list of items.
 */
@Composable
fun Modifier.controllerNav(
    itemCount: Int,
    selectedIndex: Int,
    onSelectIndex: (Int) -> Unit,
    onActivate: () -> Unit,
    onBack: () -> Unit,
    columns: Int = 1,
): Modifier = this.then(
    Modifier.onPreviewKeyEvent { event ->
        val isDown = event.nativeKeyEvent.action == KeyEvent.ACTION_DOWN
        if (!isDown) return@onPreviewKeyEvent false
        when (event.key) {
            Key.DirectionDown -> {
                val next = selectedIndex + columns
                if (next < itemCount) onSelectIndex(next)
                true
            }
            Key.DirectionUp -> {
                val prev = selectedIndex - columns
                if (prev >= 0) onSelectIndex(prev)
                true
            }
            Key.DirectionRight -> {
                val next = selectedIndex + 1
                if (next < itemCount && next % columns != 0) onSelectIndex(next)
                true
            }
            Key.DirectionLeft -> {
                val prev = selectedIndex - 1
                if (prev >= 0 && selectedIndex % columns != 0) onSelectIndex(prev)
                true
            }
            Key.Enter, Key.NumPadEnter, Key.Spacebar -> {
                onActivate()
                true
            }
            Key.Escape, Key.Back -> {
                onBack()
                true
            }
            else -> false
        }
    }
)
