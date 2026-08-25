package com.handheld.client.ui

import androidx.compose.foundation.Image
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Card
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.unit.dp
import com.handheld.client.net.ControlChannel
import com.handheld.client.net.Discovery.HostInfo
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONObject

data class Game(val id: String, val title: String, val source: String, val iconB64: String? = null)

@Composable
fun GameGrid(host: HostInfo, channel: ControlChannel?, onLaunch: (Game) -> Unit, onDesktop: () -> Unit, onBack: () -> Unit) {
    var games by remember { mutableStateOf<List<Game>?>(null) }
    var error by remember { mutableStateOf<String?>(null) }
    var selected by remember { mutableIntStateOf(0) }

    LaunchedEffect(host.ip) {
        games = null
        error = null
        channel?.connect()
        val list = withContext(Dispatchers.IO) { fetchGames(channel) }
        if (list == null) error = "Could not reach ${host.ip}" else games = list
    }

    Column(
        Modifier
            .fillMaxSize()
            .padding(16.dp)
            .controllerNav(
                itemCount = (games?.size ?: 0) + 1,   // +1 for the Desktop card
                selectedIndex = selected,
                onSelectIndex = { selected = it },
                onActivate = {
                    val g = games
                    if (g != null && selected < g.size) onLaunch(g[selected])
                    else onDesktop()
                },
                onBack = onBack,
                columns = 2,
            )
    ) {
        Text(
            "${host.name} — games",
            style = MaterialTheme.typography.headlineSmall,
            modifier = Modifier.padding(bottom = 8.dp)
        )
        val g = games
        when {
            error != null -> Text(error!!, color = MaterialTheme.colorScheme.error)
            g == null -> CircularProgressIndicator(Modifier.align(Alignment.CenterHorizontally))
            else -> LazyVerticalGrid(
                columns = GridCells.Fixed(2),
                contentPadding = PaddingValues(4.dp),
                horizontalArrangement = Arrangement.spacedBy(8.dp),
                verticalArrangement = Arrangement.spacedBy(8.dp),
                modifier = Modifier.weight(1f)
            ) {
                items(g) { game ->
                    val idx = g.indexOf(game)
                    Card(
                        Modifier
                            .fillMaxWidth()
                            .then(if (idx == selected) Modifier.border(3.dp, Color(0xFF58A6FF), RoundedCornerShape(12.dp)) else Modifier)
                            .clickable { onLaunch(game) }
                    ) {
                        Column(Modifier.padding(12.dp)) {
                            val icon = game.iconB64?.let { b64 ->
                                try {
                                    val bytes = android.util.Base64.decode(b64, android.util.Base64.DEFAULT)
                                    val bmp = android.graphics.BitmapFactory.decodeByteArray(bytes, 0, bytes.size)
                                    bmp.asImageBitmap()
                                } catch (_: Exception) { null }
                            }
                            if (icon != null) {
                                Image(
                                    bitmap = icon,
                                    contentDescription = null,
                                    modifier = Modifier.fillMaxWidth().height(90.dp)
                                )
                            }
                            Text(game.title, style = MaterialTheme.typography.titleSmall, maxLines = 2)
                            Text(
                                if (game.source == "steam") "Steam" else "Shortcut",
                                style = MaterialTheme.typography.labelSmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant
                            )
                        }
                    }
                }
                item {
                    val idx = g.size
                    Card(
                        Modifier
                            .fillMaxWidth()
                            .then(if (idx == selected) Modifier.border(3.dp, Color(0xFF58A6FF), RoundedCornerShape(12.dp)) else Modifier)
                            .clickable { onDesktop() }
                    ) {
                        Column(Modifier.padding(12.dp)) {
                            Text("🖥️ Desktop", style = MaterialTheme.typography.titleSmall)
                            Text("Stream the desktop directly", style = MaterialTheme.typography.labelSmall)
                        }
                    }
                }
            }
        }
        androidx.compose.material3.TextButton(onClick = onBack, Modifier.align(Alignment.End)) {
            Text("Back")
        }
    }
}

private fun fetchGames(channel: ControlChannel?): List<Game>? {
    if (channel == null) return null
    var result: List<Game>? = null
    channel.onMessage = { msg ->
        if (msg.optString("type") == "games") {
            val arr = msg.optJSONArray("games")
            val list = ArrayList<Game>()
            if (arr != null) {
                for (i in 0 until arr.length()) {
                    val o = arr.getJSONObject(i)
                    list.add(
                        Game(
                            o.optString("id"),
                            o.optString("title"),
                            o.optString("source"),
                            o.optString("icon", null)
                        )
                    )
                }
            }
            result = list
        }
    }
    channel.requestGames()
    val deadline = System.currentTimeMillis() + 10000
    while (result == null && System.currentTimeMillis() < deadline) {
        Thread.sleep(50)
    }
    return result
}
