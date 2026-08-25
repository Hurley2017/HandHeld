package com.handheld.client.ui

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.handheld.client.net.Discovery
import com.handheld.client.net.Discovery.HostInfo
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

@Composable
fun App() {
    var state by remember { mutableStateOf<UiState>(UiState.Searching) }
    var refreshTick by remember { mutableStateOf(0) }
    var selected by remember { mutableStateOf<HostInfo?>(null) }
    var screen by remember { mutableStateOf<Screen>(Screen.Hosts) }

    LaunchedEffect(refreshTick) {
        state = UiState.Searching
        val hosts = withContext(Dispatchers.IO) { Discovery.discover(2000) }
        state = if (hosts.isEmpty()) UiState.NoneFound else UiState.Hosts(hosts)
    }

    val host = selected
    var pendingGame by remember { mutableStateOf<String?>(null) }
    // Process-wide control channel — survives activity recreation so the WS
    // connection and host-side registration are not lost on rotation/background.
    val channel = remember(selected) {
        selected?.let { com.handheld.client.net.ControlChannelHolder.get(it.ip) }
    }
    when (screen) {
        Screen.Hosts -> Unit
        Screen.Games -> if (host != null) {
            GameGrid(
                host = host,
                channel = channel,
                onLaunch = { game ->
                    pendingGame = game.id
                    screen = Screen.Streaming
                },
                onDesktop = {
                    pendingGame = "desktop"
                    screen = Screen.Streaming
                },
                onBack = { screen = Screen.Hosts }
            )
            return
        }
        Screen.Streaming -> if (host != null && pendingGame != null) {
            StreamScreen(host, pendingGame!!, channel, onExit = { screen = Screen.Games })
            return
        }
    }

    Box(Modifier.fillMaxSize()) {
        when (val s = state) {
            is UiState.Searching -> {
                CircularProgressIndicator(Modifier.align(Alignment.Center))
                Text("Searching for hosts…", Modifier.align(Alignment.Center).padding(top = 72.dp))
            }
            is UiState.NoneFound -> {
                Text(
                    "No HandHeld hosts found on this network.\nMake sure the host app is running.",
                    Modifier.align(Alignment.Center).padding(24.dp),
                    style = MaterialTheme.typography.bodyLarge
                )
            }
            is UiState.Hosts -> {
                HostList(
                    s.hosts,
                    onSelect = { selected = it; screen = Screen.Games },
                    onRefresh = { refreshTick++ }
                )
            }
        }
    }
}

private enum class Screen { Hosts, Games, Streaming }

sealed interface UiState {
    data object Searching : UiState
    data object NoneFound : UiState
    data class Hosts(val hosts: List<HostInfo>) : UiState
}
