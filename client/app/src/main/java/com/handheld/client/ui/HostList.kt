package com.handheld.client.ui

import androidx.compose.foundation.clickable
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import com.handheld.client.net.Discovery.HostInfo
import kotlinx.coroutines.MainScope
import kotlinx.coroutines.launch

@Composable
fun HostList(hosts: List<HostInfo>, onSelect: (HostInfo) -> Unit, onRefresh: () -> Unit) {
    var selected by remember { mutableIntStateOf(0) }
    val listState = rememberLazyListState()

    Column(
        Modifier
            .fillMaxSize()
            .padding(16.dp)
            .controllerNav(
                itemCount = hosts.size,
                selectedIndex = selected,
                onSelectIndex = { i ->
                    selected = i
                    MainScope().launch { listState.animateScrollToItem(i) }
                },
                onActivate = { if (hosts.isNotEmpty()) onSelect(hosts[selected.coerceIn(0, hosts.size - 1)]) },
                onBack = { },
            )
    ) {
        Text(
            "HandHeld — choose a host",
            style = MaterialTheme.typography.headlineSmall,
            modifier = Modifier.padding(bottom = 12.dp)
        )
        LazyColumn(Modifier.weight(1f), state = listState) {
            items(hosts) { host ->
                val idx = hosts.indexOf(host)
                val isSel = idx == selected
                Card(
                    Modifier
                        .fillMaxWidth()
                        .padding(vertical = 6.dp)
                        .then(if (isSel) Modifier.border(3.dp, Color(0xFF58A6FF), RoundedCornerShape(12.dp)) else Modifier)
                        .clickable { onSelect(host) }
                ) {
                    Column(Modifier.fillMaxWidth().padding(16.dp)) {
                        Text(host.name, style = MaterialTheme.typography.titleMedium)
                        Text(host.ip, style = MaterialTheme.typography.bodySmall)
                    }
                }
            }
        }
        Button(onClick = onRefresh, Modifier.fillMaxWidth()) {
            Text("Refresh")
        }
    }
}
