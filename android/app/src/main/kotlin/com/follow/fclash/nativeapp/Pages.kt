@file:OptIn(androidx.compose.material3.ExperimentalMaterial3Api::class)
package com.follow.fclash.nativeapp

import android.content.Intent
import android.net.Uri
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.*
import androidx.compose.foundation.gestures.detectDragGesturesAfterLongPress
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.grid.*
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.material3.pulltorefresh.PullToRefreshBox
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.Layout
import androidx.compose.ui.layout.boundsInRoot
import androidx.compose.ui.layout.onGloballyPositioned
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.platform.LocalClipboardManager
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Constraints
import androidx.compose.ui.unit.dp
import com.follow.fclash.BuildConfig
import com.follow.fclash.domain.*
import com.google.gson.*
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.collect
import java.net.NetworkInterface
import kotlin.math.*

fun bytes(value: String): String { var amount = value.toDoubleOrNull() ?: 0.0; var i = 0; val units = listOf("B", "KB", "MB", "GB"); while (amount >= 1024 && i < units.lastIndex) { amount /= 1024; i++ }; return if (i == 0) "${amount.toLong()} ${units[i]}" else "%.1f %s".format(amount, units[i]) }
fun flag(code: String): String = if (code.matches(Regex("[A-Z]{2}"))) code.map { String(Character.toChars(0x1f1e6 + it.code - 'A'.code)) }.joinToString("") else ""
private val dashboardKeys = listOf("networkSpeed", "systemProxyButton", "tunButton", "outboundMode", "networkDetection", "trafficUsage", "intranetIp")
private val dashboardTitles = mapOf("networkSpeed" to "网络速度", "systemProxyButton" to "系统代理", "tunButton" to "TUN", "outboundMode" to "出站模式", "outboundModeV2" to "出站模式", "networkDetection" to "网络检测", "trafficUsage" to "流量统计", "intranetIp" to "内网 IP", "memoryInfo" to "内存信息")

@Composable fun Dashboard(ui: UiActions) {
    val s = ui.session
    var detected by remember { mutableStateOf("Timeout") }
    val history = remember { mutableStateListOf(0f) }
    LaunchedEffect(s.traffic) { history.add(s.traffic.get("down").text("0").toFloatOrNull() ?: 0f); while (history.size > 60) history.removeAt(0) }
    LaunchedEffect(s.running, s.state.currentId, s.proxies) { if (!s.safe && s.running) runCatching { val info = JsonParser.parseString(s.fetch("https://ipwho.is/").first).asJsonObject; detected = "${flag(info.get("country_code").text())} ${info.get("ip").text()}" } }
    val order = (if (s.state.settings.has("dashboardWidgets")) s.state.settings.strings("dashboardWidgets") else dashboardKeys).filter { it in dashboardTitles }
    val positions = remember { mutableMapOf<String, androidx.compose.ui.geometry.Rect>() }
    var dragged by remember { mutableStateOf<String?>(null) }; var dragOffset by remember { mutableStateOf(Offset.Zero) }; var origin by remember { mutableStateOf(Offset.Zero) }
    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(16.dp, 8.dp, 16.dp, 88.dp)) {
        Masonry(order) { key ->
            Box(Modifier.fillMaxSize().onGloballyPositioned { positions[key] = it.boundsInRoot() }.graphicsLayer { if (dragged == key) { translationX = dragOffset.x; translationY = dragOffset.y } }) {
            Surface(shape = RoundedCornerShape(14.dp), color = MaterialTheme.colorScheme.surfaceContainerLow, border = BorderStroke(.5.dp, MaterialTheme.colorScheme.surfaceContainerHighest), modifier = Modifier.fillMaxSize()) {
                Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        Icon(when (key) { "networkSpeed" -> Icons.Default.Speed; "trafficUsage" -> Icons.Default.DonutLarge; "systemProxyButton" -> Icons.Default.Shuffle; "tunButton" -> Icons.Default.ShowChart; "networkDetection" -> Icons.Default.NetworkCheck; "intranetIp" -> Icons.Default.Devices; else -> Icons.Default.AltRoute }, null, Modifier.size(20.dp))
                        Text(T(dashboardTitles.getValue(key)), style = MaterialTheme.typography.bodyMedium)
                    }
                    when (key) {
                        "networkSpeed" -> {
                            Text("↑ ${bytes(s.traffic.get("up").text("0"))}/s   ↓ ${bytes(s.traffic.get("down").text("0"))}/s", style = MaterialTheme.typography.labelSmall)
                            val color = MaterialTheme.colorScheme.primary
                            Canvas(Modifier.fillMaxWidth().weight(1f)) { val max = max(history.maxOrNull() ?: 1f, 1f); val path = Path(); history.forEachIndexed { i, value -> val x = size.width * i / max(history.size - 1, 1); val y = size.height - value / max * size.height; if (i == 0) path.moveTo(x, y) else path.lineTo(x, y) }; drawPath(path, color, style = Stroke(1.5.dp.toPx())) }
                        }
                        "systemProxyButton", "tunButton" -> Row(Modifier.fillMaxWidth().clickable { ui.push(if (key == "tunButton") "VPN" else "网络") }, verticalAlignment = Alignment.CenterVertically) {
                            Text(T("选项"), Modifier.weight(1f), style = MaterialTheme.typography.labelSmall)
                            val enabled = if (key == "tunButton") s.state.config.obj("vpnProps").flag("enable", true) else s.state.config.obj("vpnProps").flag("systemProxy", true)
                            Switch(checked = enabled, enabled = !s.safe, onCheckedChange = { value -> ui.run { s.changeConfig { if (key == "tunButton") s.state.config.obj("vpnProps").addProperty("enable", value) else s.state.config.obj("vpnProps").addProperty("systemProxy", value) } } }, modifier = Modifier.height(24.dp))
                        }
                        "outboundMode", "outboundModeV2" -> listOf("rule" to "规则", "global" to "全局", "direct" to "直连").forEach { (mode, label) ->
                            Row(Modifier.fillMaxWidth().height(32.dp).clickable { ui.run { s.changeConfig { s.state.patch.addProperty("mode", mode) } } }, verticalAlignment = Alignment.CenterVertically) { RadioButton(selected = s.state.patch.get("mode").text("rule") == mode, onClick = { ui.run { s.changeConfig { s.state.patch.addProperty("mode", mode) } } }, modifier = Modifier.size(28.dp)); Text(T(label), style = MaterialTheme.typography.bodySmall) }
                        }
                        "trafficUsage" -> {
                            val primary = MaterialTheme.colorScheme.primary; val secondary = MaterialTheme.colorScheme.secondaryContainer
                            val up = s.total.get("up").text("0").toFloatOrNull() ?: 0f; val down = s.total.get("down").text("0").toFloatOrNull() ?: 0f
                            Row(verticalAlignment = Alignment.CenterVertically) {
                                Canvas(Modifier.size(64.dp)) { drawCircle(secondary, radius = size.minDimension / 2 - 6.dp.toPx(), style = Stroke(10.dp.toPx())); if (up + down > 0) drawArc(primary, -90f, up / (up + down) * 360, false, style = Stroke(10.dp.toPx())) }
                                Column(Modifier.padding(start = 8.dp)) { Text(T("上传"), style = MaterialTheme.typography.labelSmall); Text(bytes(up.toString()), style = MaterialTheme.typography.labelSmall); Text(T("下载"), style = MaterialTheme.typography.labelSmall); Text(bytes(down.toString()), style = MaterialTheme.typography.labelSmall) }
                            }
                        }
                        "networkDetection" -> Text(detected, style = MaterialTheme.typography.bodySmall, maxLines = 1)
                        "memoryInfo" -> Text(bytes(s.memory), style = MaterialTheme.typography.bodySmall)
                        "intranetIp" -> Text(remember { runCatching { NetworkInterface.getNetworkInterfaces().toList().filterNot { it.isLoopback || !it.isUp }.flatMap { it.inetAddresses.toList() }.firstOrNull { it is java.net.Inet4Address && it.isSiteLocalAddress }?.hostAddress ?: "—" }.getOrDefault("—") }, style = MaterialTheme.typography.bodySmall, maxLines = 1)
                    }
                }
            }
            if (ui.dashboardEditing) {
                Box(Modifier.matchParentSize().background(MaterialTheme.colorScheme.primary.copy(alpha = .04f)).pointerInput(key, order) {
                    detectDragGesturesAfterLongPress(onDragStart = { at -> dragged = key; origin = (positions[key]?.topLeft ?: Offset.Zero) + at; dragOffset = Offset.Zero }, onDragCancel = { dragged = null; dragOffset = Offset.Zero }, onDragEnd = {
                        val at = origin + dragOffset
                        val destination = positions.entries.firstOrNull { it.key != key && it.value.contains(at) }?.key
                        dragged = null; dragOffset = Offset.Zero
                        if (destination != null) { val next = order.toMutableList(); next.remove(key); next.add(order.indexOf(destination).coerceIn(0, next.size), key); s.state.settings.add("dashboardWidgets", strings(next)); ui.run { s.save() } }
                    }, onDrag = { change, amount -> change.consume(); dragOffset += amount })
                })
                IconButton(onClick = { s.state.settings.add("dashboardWidgets", strings(order.filterNot { it == key })); ui.run { s.save() } }, modifier = Modifier.align(Alignment.TopEnd).size(28.dp)) { Icon(Icons.Default.RemoveCircleOutline, T("移除"), Modifier.size(18.dp)) }
            }
            }
        }
    }
}
@Composable private fun Masonry(order: List<String>, content: @Composable (String) -> Unit) {
    val density = LocalDensity.current
    Layout(content = { order.forEach { key -> Box { content(key) } } }) { children, constraints ->
        val gap = 14.dp.roundToPx(); val columns = max(4 * ceil(with(density) { constraints.maxWidth.toDp().value } / 280).toInt(), 8)
        val stride = (constraints.maxWidth + gap).toDouble() / columns; val offsets = IntArray(columns)
        val placed = children.mapIndexed { index, child ->
            val key = order[index]; val span = min(if (key in listOf("networkSpeed", "outboundModeV2")) 8 else 4, columns)
            val start = (0..columns - span).minBy { offsets.slice(it until it + span).max() }; val y = offsets.slice(start until start + span).max()
            val height = (if (key in listOf("networkSpeed", "outboundMode", "trafficUsage")) 174.dp else 80.dp).roundToPx()
            val width = (span * stride - gap).toInt().coerceAtLeast(1); val item = child.measure(Constraints.fixed(width, height))
            (start until start + span).forEach { offsets[it] = y + height + gap }; Triple(item, (start * stride).toInt(), y)
        }
        layout(constraints.maxWidth, max(0, (offsets.maxOrNull() ?: gap) - gap)) { placed.forEach { (child, x, y) -> child.placeRelative(x, y) } }
    }
}
fun currentNodes(s: NativeSession, group: String): List<String> = (s.proxies.get(if (group.isBlank()) s.groups.firstOrNull() ?: "" else group) as? JsonObject)?.strings("all") ?: emptyList()
fun activeProxyGroup(ui: UiActions): String = if (ui.session.state.config.obj("proxiesStyleProps").get("type").text() == "list") ui.visibleProxyGroup else ui.page.key
private data class ProxyEntry(val group: String, val node: String? = null)
@Composable fun Proxies(ui: UiActions) {
    val s = ui.session; val style = s.state.config.obj("proxiesStyleProps")
    val group = ui.page.key.takeIf { it in s.groups } ?: s.groups.firstOrNull() ?: ""
    var search by remember { mutableStateOf("") }
    val listMode = style.get("type").text("tab") == "list"
    val entries = (if (listMode) s.groups else listOf(group)).flatMap { name ->
        val sorted = currentNodes(s, name).filter { it.contains(search, true) }.let { nodes -> when (style.get("sortType").text()) { "name" -> nodes.sorted(); "delay" -> nodes.sortedBy { s.delays[it]?.takeIf { it > 0 } ?: Int.MAX_VALUE }; else -> nodes } }
        (if (listMode) listOf(ProxyEntry(name)) else emptyList()) + sorted.map { ProxyEntry(name, it) }
    }
    val grid = rememberLazyGridState()
    LaunchedEffect(listMode, entries) {
        if (listMode) snapshotFlow { grid.firstVisibleItemIndex }.collect { index -> entries.getOrNull(index)?.let { ui.visibleProxyGroup = it.group } }
    }
    Column(Modifier.fillMaxSize()) {
        if (!listMode && s.groups.isNotEmpty()) ScrollableTabRow(selectedTabIndex = s.groups.indexOf(group).coerceAtLeast(0), edgePadding = 16.dp) { s.groups.forEach { name -> Tab(selected = name == group, onClick = { ui.path[ui.path.lastIndex] = ui.page.copy(key = name) }, text = { Text(name) }) } }
        if (ui.searchVisible) OutlinedTextField(search, { search = it }, Modifier.fillMaxWidth().padding(16.dp, 8.dp), label = { Text(T("搜索代理")) }, singleLine = true)
        PullToRefreshBox(isRefreshing = s.testing, onRefresh = { ui.run { s.test(currentNodes(s, activeProxyGroup(ui))) } }, modifier = Modifier.fillMaxSize()) {
            BoxWithConstraints(Modifier.fillMaxSize()) {
                val count = max(ceil(maxWidth.value / 250).toInt(), 2) + when (style.get("layout").text("standard")) { "tight" -> 1; "loose" -> -1; else -> 0 }
                LazyVerticalGrid(GridCells.Fixed(count.coerceAtLeast(1)), state = grid, contentPadding = PaddingValues(16.dp, 16.dp, 16.dp, 96.dp), horizontalArrangement = Arrangement.spacedBy(14.dp), verticalArrangement = Arrangement.spacedBy(14.dp), modifier = Modifier.fillMaxSize().testTag("proxy-grid")) {
                    items(entries, key = { "${it.group.length}:${it.group}:${it.node?.length ?: -1}:${it.node ?: ""}" }, span = { if (it.node == null) GridItemSpan(maxLineSpan) else GridItemSpan(1) }) { entry ->
                        if (entry.node == null) {
                            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                                Text(entry.group, Modifier.weight(1f), style = MaterialTheme.typography.titleMedium)
                                IconButton(enabled = !s.safe, onClick = { ui.run { s.test(currentNodes(s, entry.group)) } }) { Icon(Icons.Default.Speed, T("延迟测试")) }
                            }
                        } else {
                        val name = entry.node
                        val selected = (s.proxies.get(entry.group) as? JsonObject)?.get("now").text() == name
                        val shrink = style.get("cardType").text() == "shrink"
                        Surface(onClick = { ui.run { s.select(entry.group, name) } }, modifier = Modifier.testTag("proxy/${entry.group}/$name"), shape = RoundedCornerShape(14.dp), color = if (selected) MaterialTheme.colorScheme.secondaryContainer else MaterialTheme.colorScheme.surfaceContainerLow, border = BorderStroke(if (selected) 1.dp else .5.dp, if (selected) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.surfaceContainerHighest)) {
                            Column(Modifier.padding(12.dp).heightIn(min = if (shrink) 64.dp else 86.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                                Text(name, maxLines = 2, overflow = TextOverflow.Ellipsis, style = MaterialTheme.typography.bodyMedium, modifier = Modifier.heightIn(min = 36.dp))
                                if (!shrink) Text((s.proxies.get(name) as? JsonObject)?.get("type").text(), style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.Bottom) {
                                    val delay = s.delays[name]; Text(if (delay == null) "—" else if (delay < 0) T("超时") else "$delay ms", color = if (delay != null && delay in 0..499) Color(0xff4caf50) else MaterialTheme.colorScheme.onSurfaceVariant, style = MaterialTheme.typography.labelSmall)
                                    Text(flag(s.state.currentId?.let { s.state.countries[s.state.countryKey(it, name)] } ?: ""))
                                }
                            }
                        }
                        }
                    }
                }
            }
        }
    }
}
fun proxyOptions(ui: UiActions) {
    val style = ui.session.state.config.obj("proxiesStyleProps")
    ui.edit("设置", listOf(Field("type", "风格", style.get("type").text("tab"), listOf("tab", "list")), Field("sort", "排序", style.get("sortType").text("none"), listOf("none", "delay", "name")), Field("layout", "布局", style.get("layout").text("standard"), listOf("tight", "standard", "loose")), Field("card", "卡片样式", style.get("cardType").text("expand"), listOf("expand", "shrink")))) { style.addProperty("type", it["type"]); style.addProperty("sortType", it["sort"]); style.addProperty("layout", it["layout"]); style.addProperty("cardType", it["card"]); ui.session.save() }
}
@Composable fun Profiles(ui: UiActions) {
    val s = ui.session; var exporting by remember { mutableStateOf<Profile?>(null) }
    val export = rememberLauncherForActivityResult(ActivityResultContracts.CreateDocument("application/yaml")) { uri -> exporting?.let { profile -> if (uri != null) ui.run { withContext(Dispatchers.IO) { s.context.contentResolver.openOutputStream(uri)!!.use { output -> s.store.profile(profile.id).inputStream().use { it.copyTo(output) } } } } } }
    BoxWithConstraints {
        LazyVerticalGrid(GridCells.Fixed(max(floor(maxWidth.value / 280).toInt(), 1)), contentPadding = PaddingValues(16.dp, 16.dp, 16.dp, 96.dp), horizontalArrangement = Arrangement.spacedBy(14.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
            items(s.state.profiles.toList(), key = { it.id }) { profile ->
                var menu by remember { mutableStateOf(false) }; val selected = s.state.currentId == profile.id
                Surface(onClick = { ui.run { s.switchProfile(profile) } }, shape = RoundedCornerShape(14.dp), color = if (selected) MaterialTheme.colorScheme.secondaryContainer else MaterialTheme.colorScheme.surfaceContainerLow, border = BorderStroke(if (selected) 1.dp else .5.dp, if (selected) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.surfaceContainerHighest)) {
                    Row(Modifier.padding(12.dp).heightIn(min = 64.dp), verticalAlignment = Alignment.CenterVertically) {
                        RadioButton(selected, onClick = { ui.run { s.switchProfile(profile) } })
                        Column(Modifier.weight(1f)) { Text(profile.label, maxLines = 2); Text(profile.updatedAt ?: "", style = MaterialTheme.typography.labelSmall) }
                        Box { IconButton(onClick = { menu = true }) { Icon(Icons.Default.MoreVert, T("更多")) }
                            DropdownMenu(menu, onDismissRequest = { menu = false }) {
                                fun close(action: () -> Unit) { menu = false; action() }
                                DropdownMenuItem(text = { Text(T("编辑")) }, onClick = { close { editProfile(ui, profile) } })
                                if (profile.url.isNotBlank()) DropdownMenuItem(text = { Text(T("更新")) }, onClick = { close { ui.run { s.update(profile) } } })
                                DropdownMenuItem(text = { Text(T("覆写")) }, onClick = { close { ui.push(profile.label, "覆写", profile = profile.id) } })
                                DropdownMenuItem(text = { Text(T("编辑 YAML")) }, onClick = { close { ui.edit("编辑 YAML", listOf(Field("yaml", "YAML", s.store.profile(profile.id).readText(), multiline = true))) { values -> val source = values.getValue("yaml"); Configuration.parse(source); val old = s.store.profile(profile.id).readText(); try { withContext(Dispatchers.IO) { s.store.atomic(s.store.profile(profile.id), source) }; if (selected) s.apply() } catch (error: Exception) { s.store.atomic(s.store.profile(profile.id), old); throw error } } } })
                                DropdownMenuItem(text = { Text(T("导出文件")) }, onClick = { close { exporting = profile; export.launch("${profile.label}.yaml") } })
                                DropdownMenuItem(text = { Text(T("删除")) }, onClick = { close { ui.confirm("确定删除“${profile.label}”？") { if (selected) { if (s.running) s.stop(); s.state.currentId = null }; s.state.profiles.remove(profile); s.save() } } })
                            }
                        }
                    }
                }
            }
        }
    }
}
fun editProfile(ui: UiActions, profile: Profile) { ui.edit("编辑配置", listOf(Field("name", "名称", profile.label), Field("url", "URL", profile.url), Field("auto", "自动更新", profile.autoUpdate.toString(), listOf("true", "false")), Field("interval", "更新间隔（小时）", (profile.updateInterval / 3600000).toString()))) { values -> val interval = values.getValue("interval").toLong(); require(interval in 1..8760); profile.label = values.getValue("name").ifBlank { "新配置" }; profile.url = values.getValue("url"); profile.autoUpdate = values["auto"] == "true"; profile.updateInterval = interval * 3600000; ui.session.save() } }
@Composable fun Tools(ui: UiActions) { SettingsList {
    Section("更多"); listOf("请求" to Icons.Default.ViewTimeline, "连接" to Icons.Default.Ballot, "资源" to Icons.Default.Storage, "日志" to Icons.Default.Adb).forEach { (title, icon) -> Setting(title, icon = icon) { ui.push(title) } }
    Section("设置"); Setting("语言", ui.session.state.settings.get("locale").text("默认"), Icons.Default.Language) { ui.edit("语言", listOf(Field("locale", "语言", ui.session.state.settings.get("locale").text("system"), listOf("system", "zh_CN", "en", "ja", "ru")))) { ui.session.state.settings.addProperty("locale", it["locale"]); ui.session.save() } }
    listOf("主题" to Icons.Default.Palette, "备份与恢复" to Icons.Default.CloudSync, "基本配置" to Icons.Default.Edit, "进阶配置" to Icons.Default.Construction, "应用" to Icons.Default.Settings).forEach { (title, icon) -> Setting(title, icon = icon) { ui.push(title) } }
    Section("其他"); Setting("关于", icon = Icons.Default.Info) { ui.push("关于") }
} }
@Composable fun Advanced(ui: UiActions) { SettingsList { listOf("网络", "VPN", "访问控制", "按需连接", "DNS", "附加规则", "脚本").forEach { title -> Setting(title) { ui.push(title) }; HorizontalDivider(Modifier.padding(horizontal = 16.dp), thickness = .5.dp) } } }
@Composable fun History(ui: UiActions) {
    val s = ui.session; var search by remember { mutableStateOf("") }; val clipboard = LocalClipboardManager.current
    Column { OutlinedTextField(search, { search = it }, Modifier.fillMaxWidth().padding(16.dp, 8.dp), label = { Text(T("搜索")) }); LazyColumn(contentPadding = PaddingValues(16.dp)) { items((if (ui.page.kind == "日志") s.logs else s.requests).toList().filter { it.contains(search, true) }) { text -> Text(text, Modifier.fillMaxWidth().combinedClickable(onClick = { ui.message = text }, onLongClick = { clipboard.setText(AnnotatedString(text)) }).padding(vertical = 8.dp), style = MaterialTheme.typography.bodySmall, fontFamily = FontFamily.Monospace) } } }
}
@Composable fun Connections(ui: UiActions) {
    val s = ui.session; var search by remember { mutableStateOf("") }
    Column {
        Row(Modifier.padding(16.dp), verticalAlignment = Alignment.CenterVertically) { OutlinedTextField(search, { search = it }, Modifier.weight(1f), label = { Text(T("搜索")) }); IconButton(onClick = { ui.confirm("关闭全部连接？") { s.rpc("closeConnections"); s.poll("连接") } }) { Icon(Icons.Default.Close, T("关闭全部连接")) } }
        LazyColumn { items(s.connections.toList().filter { it.toString().contains(search, true) }) { node -> val info = node.asJsonObject; val metadata = info.getAsJsonObject("metadata") ?: JsonObject(); Setting(metadata.get("host").text(metadata.get("destinationIP").text()), "${metadata.get("network").text()} · ${info.get("chains").text()}", trailing = { IconButton(onClick = { ui.run { s.rpc("closeConnection", info.get("id").text()); s.poll("连接") } }) { Icon(Icons.Default.Close, T("关闭")) } }) { ui.message = json.toJson(info) } } }
    }
}
@Composable fun Resources(ui: UiActions) {
    val s = ui.session; var providers by remember { mutableStateOf<List<JsonElement>>(emptyList()) }
    LaunchedEffect(Unit) { if (!s.safe) runCatching { val data = s.decode(s.rpc("getExternalProviders")); providers = if (data.isJsonArray) data.asJsonArray.toList() else data.asJsonObject.entrySet().map { it.value } }.onFailure(s::report) }
    LazyColumn { items(providers) { node -> val entry = node.asJsonObject; Setting(entry.get("name").text(), "${entry.get("type").text()} · ${entry.get("count").text()}", trailing = { IconButton(onClick = { ui.run { val result = s.rpc("updateExternalProvider", entry.get("name").text()).text(); check(result.isEmpty()) { result } } }) { Icon(Icons.Default.Sync, T("更新")) } }) } }
}
@Composable fun About(ui: UiActions) { SettingsList {
    Row(Modifier.padding(24.dp, 40.dp), verticalAlignment = Alignment.CenterVertically) { Image(androidx.compose.ui.res.painterResource(com.follow.fclash.R.mipmap.ic_launcher), "FClash", Modifier.size(64.dp)); Column(Modifier.padding(start = 24.dp)) { Text("FClash", style = MaterialTheme.typography.headlineMedium); Text(BuildConfig.VERSION_NAME) } }
    Text(T("基于 ClashMeta 的多平台代理客户端，简单易用，开源无广告。"), Modifier.padding(24.dp))
    Setting("检查更新") { ui.run { val data = JsonParser.parseString(ui.session.fetch("https://api.github.com/repos/yangthen/FClash/releases/latest").first).asJsonObject; ui.message = "${data.get("tag_name").text()}\n${data.get("body").text()}" } }
} }
