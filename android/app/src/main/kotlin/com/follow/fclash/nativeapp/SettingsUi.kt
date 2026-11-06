@file:OptIn(androidx.compose.material3.ExperimentalMaterial3Api::class)
package com.follow.fclash.nativeapp

import android.content.Intent
import kotlin.math.abs
import androidx.compose.foundation.*
import androidx.compose.foundation.gestures.detectDragGesturesAfterLongPress
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.Alignment
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.unit.dp
import com.follow.fclash.domain.*
import com.google.gson.*
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.File
import java.net.HttpURLConnection
import java.net.URL
import java.util.Base64

@Composable fun Settings(ui: UiActions) {
    val s = ui.session; val state = s.state
    SettingsList {
        when (ui.page.kind) {
            "DNS" -> {
                SwitchSetting(ui, "覆盖 DNS", state.config.flag("overrideDns")) { state.config.addProperty("overrideDns", it) }
                val dns = state.patch.get("dns") as? JsonObject ?: Configuration.defaultDns()
                fun set(key: String, value: JsonElement) { state.patch.obj("dns").add(key, value) }
                Section("选项")
                for ((key, title, default) in listOf(Triple("enable", "状态", true), Triple("use-hosts", "使用 Hosts", true), Triple("use-system-hosts", "使用系统 Hosts", true), Triple("ipv6", "IPv6", false), Triple("respect-rules", "遵循规则", false), Triple("prefer-h3", "PreferH3", false))) SwitchSetting(ui, title, dns.flag(key, default)) { set(key, JsonPrimitive(it)) }
                InputSetting(ui, "监听地址", dns.get("listen").text("127.0.0.1:1053")) { set("listen", JsonPrimitive(it)) }
                ChoiceSetting(ui, "模式", dns.get("enhanced-mode").text("fake-ip"), listOf("fake-ip", "redir-host")) { set("enhanced-mode", JsonPrimitive(it)) }
                InputSetting(ui, "Fake-IP 范围", dns.get("fake-ip-range").text("198.18.0.1/16")) { set("fake-ip-range", JsonPrimitive(it)) }
                for ((key, title) in listOf("fake-ip-filter" to "Fake-IP 过滤", "default-nameserver" to "默认域名服务器", "nameserver-policy" to "域名策略", "nameserver" to "域名服务器", "fallback" to "Fallback", "proxy-server-nameserver" to "代理域名服务器", "direct-nameserver" to "直连域名服务器")) Setting(title, if (key == "nameserver-policy") "${Configuration.policyMap(dns.get(key)).size()} 项" else dns.strings(key).joinToString(", ")) { ui.push(title, if (key == "nameserver-policy") "域名策略" else "DNS列表", key) }
                Section("Fallback 过滤"); val filter = dns.get("fallback-filter") as? JsonObject ?: JsonObject()
                SwitchSetting(ui, "GEOIP", filter.flag("geoip", true)) { state.patch.obj("dns").obj("fallback-filter").addProperty("geoip", it) }
                InputSetting(ui, "GEOIP 代码", filter.get("geoip-code").text("CN")) { state.patch.obj("dns").obj("fallback-filter").addProperty("geoip-code", it) }
                for (key in listOf("geosite", "ipcidr", "domain")) Setting(key.uppercase(), filter.strings(key).joinToString(", ")) { ui.push(key.uppercase(), "DNS列表", "fallback-filter/$key") }
            }
            "基本配置" -> {
                ChoiceSetting(ui, "日志等级", state.patch.get("log-level").text("error"), listOf("debug", "info", "warning", "error", "silent")) { state.patch.addProperty("log-level", it) }
                InputSetting(ui, "User-Agent", state.patch.get("global-ua").text("FClash")) { state.patch.addProperty("global-ua", it) }
                for ((key, title) in listOf("ipv6" to "IPv6", "allow-lan" to "允许局域网访问", "unified-delay" to "统一延迟", "tcp-concurrent" to "TCP 并发")) SwitchSetting(ui, title, state.patch.flag(key, key in listOf("unified-delay", "tcp-concurrent"))) { state.patch.addProperty(key, it) }
                for ((key, title) in listOf("mixed-port" to "混合端口", "port" to "HTTP 端口", "socks-port" to "SOCKS 端口")) InputSetting(ui, title, state.patch.number(key, if (key == "mixed-port") 7890 else 0).toString()) { val port = it.toInt(); require(port in 0..65535 && (port != 0 || key != "mixed-port")); state.patch.addProperty(key, port) }
                ChoiceSetting(ui, "进程匹配", state.patch.get("find-process-mode").text("always"), listOf("always", "strict", "off")) { state.patch.addProperty("find-process-mode", it) }
                ChoiceSetting(ui, "GEO 文件加载模式", state.patch.get("geodata-loader").text("memconservative"), listOf("memconservative", "standard")) { state.patch.addProperty("geodata-loader", it) }
            }
            "网络" -> {
                SwitchSetting(ui, "系统代理", state.config.obj("vpnProps").flag("systemProxy", true)) { state.config.obj("vpnProps").addProperty("systemProxy", it) }
                SwitchSetting(ui, "附加系统 DNS", state.network.flag("appendSystemDns")) { state.network.addProperty("appendSystemDns", it) }
                Setting("绕过域名", state.network.strings("bypassDomain").joinToString(", ")) { ui.push("绕过域名", "DNS列表", "network/bypassDomain") }
                Setting("Hosts") { ui.edit("Hosts", listOf(Field("hosts", "YAML", Configuration.yaml(JsonObject().apply { add("hosts", state.patch.get("hosts")?.deepCopy() ?: JsonObject()) }), multiline = true))) { values -> s.changeConfig { state.patch.add("hosts", Configuration.parse(values.getValue("hosts")).getAsJsonObject("hosts")) } } }
                InputSetting(ui, "延迟测试地址", state.settings.get("testUrl").text("https://www.gstatic.com/generate_204")) { require(it.startsWith("https://") || it.startsWith("http://")); state.settings.addProperty("testUrl", it) }
            }
            "VPN" -> {
                val vpn = state.config.obj("vpnProps")
                SwitchSetting(ui, "VPN", vpn.flag("enable", true)) { vpn.addProperty("enable", it) }
                SwitchSetting(ui, "DNS 劫持", vpn.flag("dnsHijacking")) { vpn.addProperty("dnsHijacking", it) }
                SwitchSetting(ui, "IPv6", vpn.flag("ipv6")) { vpn.addProperty("ipv6", it) }
                SwitchSetting(ui, "允许绕过 VPN", vpn.flag("allowBypass", true)) { vpn.addProperty("allowBypass", it) }
                ChoiceSetting(ui, "协议栈", state.patch.obj("tun").get("stack").text("mixed"), listOf("mixed", "system", "gvisor")) { state.patch.obj("tun").addProperty("stack", it) }
                Setting("路由地址", state.patch.obj("tun").strings("route-address").joinToString(", ")) { ui.push("路由地址", "DNS列表", "tun/route-address") }
                Text(T("VPN 选项在下次启动服务时生效"), Modifier.padding(16.dp), style = MaterialTheme.typography.bodySmall)
            }
            "访问控制" -> AccessControl(ui)
            "应用" -> {
                for ((key, title) in listOf("autoRun" to "自动运行", "autoLaunch" to "开机启动", "openLogs" to "日志", "onlyStatisticsProxy" to "仅统计代理流量")) SwitchSetting(ui, title, state.settings.flag(key)) { state.settings.addProperty(key, it) }
                Setting("忽略电池优化") { if (!s.safe) s.context.startActivity(Intent(android.provider.Settings.ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)) }
            }
            "主题" -> {
                ChoiceSetting(ui, "主题模式", state.config.obj("themeProps").get("themeMode").text("system"), listOf("system", "light", "dark")) { state.config.obj("themeProps").addProperty("themeMode", it) }
                Section("主题色")
                Row(Modifier.horizontalScroll(rememberScrollState()).padding(16.dp), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                    for (seed in listOf(0xffd8c0c3, 0xff6750a4, 0xff006c4c, 0xff0061a4, 0xff984061, 0xff805600)) Surface(onClick = { ui.run { state.config.obj("themeProps").addProperty("primaryColor", seed); s.save() } }, color = Color(seed), shape = androidx.compose.foundation.shape.CircleShape, modifier = Modifier.size(40.dp)) { }
                }
            }
            "覆写" -> {
                val profile = state.profiles.firstOrNull { it.id == ui.page.profileId }
                if (profile != null) {
                    Setting("覆写方式", profile.overwriteType) { ui.edit("覆写方式", listOf(Field("type", "覆写方式", profile.overwriteType, listOf("standard", "script", "custom")))) { values -> val previous = profile.overwriteType; try { profile.overwriteType = values.getValue("type"); if (state.currentId == profile.id) s.apply() else s.save() } catch (error: Exception) { profile.overwriteType = previous; throw error } } }
                    Setting("脚本", state.scripts.firstOrNull { it.id == profile.scriptId }?.label ?: "未设置") { val choices = listOf("未设置") + state.scripts.map { it.label }; ui.edit("脚本", listOf(Field("script", "脚本", state.scripts.firstOrNull { it.id == profile.scriptId }?.label ?: "未设置", choices))) { values -> val old = profile.scriptId; try { profile.scriptId = state.scripts.firstOrNull { it.label == values["script"] }?.id; if (state.currentId == profile.id) s.apply() else s.save() } catch (error: Exception) { profile.scriptId = old; throw error } } }
                    for ((key, title) in listOf("added" to "附加规则", "custom" to "自定义规则", "disabled" to "禁用的附加规则")) Setting(title) { ui.push(title, "规则列表", key, profile.id) }
                    Setting("自定义代理分组") { ui.edit("自定义代理分组", listOf(Field("groups", "YAML", Configuration.yaml(JsonObject().apply { add("proxy-groups", profile.groups.deepCopy()) }), multiline = true))) { values -> val next = Configuration.parse(values.getValue("groups")).getAsJsonArray("proxy-groups"); val old = profile.groups; try { profile.groups = next; if (state.currentId == profile.id) s.apply() else s.save() } catch (e: Exception) { profile.groups = old; throw e } } }
                }
            }
        }
    }
}
@Composable private fun SwitchSetting(ui: UiActions, title: String, value: Boolean, change: (Boolean) -> Unit) { Setting(title, trailing = { Switch(value, onCheckedChange = { next -> ui.run { ui.session.changeConfig { change(next) } } }) }) }
@Composable private fun InputSetting(ui: UiActions, title: String, value: String, change: (String) -> Unit) { Setting(title, value) { ui.edit(title, listOf(Field("value", title, value))) { values -> ui.session.changeConfig { change(values.getValue("value").trim()) } } } }
@Composable private fun ChoiceSetting(ui: UiActions, title: String, value: String, choices: List<String>, change: (String) -> Unit) { Setting(title, value) { ui.edit(title, listOf(Field("value", title, value, choices))) { values -> ui.session.changeConfig { change(values.getValue("value")) } } } }
@Composable private fun AccessControl(ui: UiActions) {
    val s = ui.session; val access = s.state.config.obj("vpnProps").obj("accessControlProps")
    SwitchSetting(ui, "启用访问控制", access.flag("enable")) { access.addProperty("enable", it) }
    ChoiceSetting(ui, "模式", access.get("mode").text("rejectSelected"), listOf("acceptSelected", "rejectSelected")) { access.addProperty("mode", it) }
    var search by remember { mutableStateOf("") }; var packages by remember { mutableStateOf<List<Pair<String, String>>>(emptyList()) }
    LaunchedEffect(Unit) { packages = withContext(Dispatchers.IO) { @Suppress("DEPRECATION") s.context.packageManager.getInstalledApplications(0).map { it.packageName to s.context.packageManager.getApplicationLabel(it).toString() }.sortedBy { it.second } } }
    OutlinedTextField(search, { search = it }, Modifier.padding(16.dp).fillMaxWidth(), label = { Text(T("搜索")) })
    val key = if (access.get("mode").text("rejectSelected") == "acceptSelected") "acceptList" else "rejectList"
    packages.filter { it.first.contains(search, true) || it.second.contains(search, true) }.forEach { (name, label) ->
        val selected = name in access.strings(key)
        Setting(label, name, trailing = { Checkbox(selected, { checked -> ui.run { s.changeConfig { access.add(key, strings(access.strings(key).filterNot { it == name } + if (checked) listOf(name) else emptyList())) } } }) })
    }
}

private data class ListSpec(val values: List<String>, val save: suspend (List<String>) -> Unit)
private fun listSpec(ui: UiActions): ListSpec {
    val s = ui.session; val state = s.state
    return when (ui.page.kind) {
        "配置排序" -> ListSpec(state.profiles.map { it.id.toString() }) { ids -> val previous = state.profiles.toList(); state.profiles.clear(); state.profiles.addAll(ids.map { id -> previous.first { it.id.toString() == id } }); s.save() }
        "仪表盘排序" -> ListSpec(state.settings.strings("dashboardWidgets").ifEmpty { listOf("networkSpeed", "systemProxyButton", "tunButton", "outboundMode", "networkDetection", "trafficUsage", "intranetIp") }) { state.settings.add("dashboardWidgets", strings(it)); s.save() }
        "按需连接" -> ListSpec(state.config.strings("excludeSSIDs")) { state.config.add("excludeSSIDs", strings(it)); s.save() }
        "脚本" -> ListSpec(state.scripts.map { it.id.toString() }) { ids -> state.scripts.retainAll { it.id.toString() in ids }; s.save() }
        "域名策略" -> {
            val dns = state.patch.get("dns") as? JsonObject ?: Configuration.defaultDns()
            val policies = Configuration.policyMap(dns.get("nameserver-policy"))
            ListSpec(policies.entrySet().map { "${it.key} => ${if (it.value is JsonArray) it.value.asJsonArray.joinToString(", ") { server -> server.text() } else it.value.text()}" }) { values ->
                val map = JsonObject(); values.forEach { raw -> val parts = raw.split(" => ", limit = 2); require(parts.size == 2 && !map.has(parts[0])) { "策略重复或无效" }; map.add(parts[0], if (parts[1].startsWith('[')) JsonParser.parseString(parts[1]) else JsonPrimitive(parts[1])) }
                s.changeConfig { state.patch.obj("dns").add("nameserver-policy", map) }
            }
        }
        "DNS列表" -> {
            val parts = ui.page.key.split('/'); val key = parts.last()
            val parent = when (parts.first()) { "network" -> state.network; "tun" -> state.patch.obj("tun"); "fallback-filter" -> state.patch.obj("dns").obj("fallback-filter"); else -> state.patch.get("dns") as? JsonObject ?: Configuration.defaultDns() }
            ListSpec(parent.strings(key)) { values -> s.changeConfig { val target = if (parts.size == 1) state.patch.obj("dns") else parent; target.add(key, strings(values)) } }
        }
        else -> {
            val profile = state.profiles.firstOrNull { it.id == ui.page.profileId }
            val source = if (profile == null) state.addedRules else when (ui.page.key) { "custom" -> profile.customRules; "disabled" -> profile.disabledRules; else -> profile.addedRules }
            ListSpec(source.toList()) { values -> val old = source.toList(); source.clear(); source.addAll(values); try { if (state.current != null) s.apply() else s.save() } catch (error: Exception) { source.clear(); source.addAll(old); throw error } }
        }
    }
}
@Composable fun OrderedPage(ui: UiActions) {
    val spec = listSpec(ui)
    val values = remember(ui.page, spec.values) { spec.values.toMutableStateList() }
    var dragging by remember { mutableIntStateOf(-1) }; var offset by remember { mutableFloatStateOf(0f) }
    val rowHeight = with(LocalDensity.current) { 72.dp.toPx() }
    LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(bottom = 88.dp)) {
        if (ui.page.kind == "按需连接") item { Setting("位置信息权限", "读取 Wi-Fi 名称需要位置权限及系统定位服务") { if (!ui.session.safe) ui.requestLocation() } }
        itemsIndexed(values) { index, raw ->
            val rule = ui.page.kind in listOf("附加规则", "规则列表")
            val title = when { ui.page.kind == "配置排序" -> ui.session.state.profiles.firstOrNull { it.id.toString() == raw }?.label ?: raw; ui.page.kind == "脚本" -> ui.session.state.scripts.firstOrNull { it.id.toString() == raw }?.label ?: raw; rule -> raw.substringBefore(',').replace('-', '_'); else -> raw.substringBefore(" => ") }
            val detail = if (rule) raw.split(',').getOrNull(1) else raw.takeIf { " => " in it }?.substringAfter(" => ")
            ListItem(headlineContent = { Text(title) }, supportingContent = detail?.let { { Text(it) } }, modifier = Modifier.background(if (dragging == index) MaterialTheme.colorScheme.secondaryContainer else Color.Transparent).clickable { editEntry(ui, raw, index) }.heightIn(min = 72.dp), trailingContent = {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    IconButton(onClick = { if (index > 0) { values.add(index - 1, values.removeAt(index)); ui.run { spec.save(values.toList()) } } }, enabled = index > 0) { Icon(Icons.Default.KeyboardArrowUp, T("上移")) }
                    IconButton(onClick = { if (index < values.lastIndex) { values.add(index + 1, values.removeAt(index)); ui.run { spec.save(values.toList()) } } }, enabled = index < values.lastIndex) { Icon(Icons.Default.KeyboardArrowDown, T("下移")) }
                    Icon(Icons.Default.DragHandle, T("拖动排序"), Modifier.padding(8.dp).pointerInput(ui.page) {
                        detectDragGesturesAfterLongPress(onDragStart = { dragging = index; offset = 0f }, onDragEnd = { dragging = -1; ui.run { spec.save(values.toList()) } }, onDragCancel = { dragging = -1 }, onDrag = { change, amount ->
                            change.consume(); offset += amount.y
                            if (abs(offset) >= rowHeight && dragging in values.indices) { val next = (dragging + if (offset > 0) 1 else -1).coerceIn(0, values.lastIndex); if (next != dragging) { values.add(next, values.removeAt(dragging)); dragging = next; offset = 0f } }
                        })
                    })
                    if (ui.page.kind !in listOf("配置排序", "仪表盘排序")) IconButton(onClick = { ui.confirm("确定删除此项？") { spec.save(values.filterIndexed { i, _ -> i != index }) } }) { Icon(Icons.Default.DeleteOutline, T("删除")) }
                }
            }, colors = ListItemDefaults.colors(containerColor = Color.Transparent))
        }
        if (ui.page.kind == "仪表盘排序") item { Setting("添加") { ui.edit("添加", listOf(Field("key", "卡片", "memoryInfo", listOf("networkSpeed", "systemProxyButton", "tunButton", "outboundMode", "networkDetection", "trafficUsage", "intranetIp", "memoryInfo")))) { spec.save((values + it.getValue("key")).distinct()) } } }
    }
}
fun addListEntry(ui: UiActions) = editEntry(ui, "", -1)
private fun editEntry(ui: UiActions, raw: String, index: Int) {
    val spec = listSpec(ui)
    fun commit(value: String) { ui.run { val next = spec.values.toMutableList(); if (index < 0) next.add(value) else next[index] = value; spec.save(next) } }
    when (ui.page.kind) {
        "配置排序", "仪表盘排序" -> Unit
        "脚本" -> {
            val s = ui.session; val script = s.state.scripts.firstOrNull { it.id.toString() == raw } ?: Script()
            val body = s.store.script(script.id).takeIf { it.exists() }?.readText() ?: "function main(config) {\n  return config;\n}"
            ui.edit("脚本", listOf(Field("name", "名称", script.label), Field("script", "JavaScript", body, multiline = true))) { values -> require(values.getValue("name").isNotBlank()); val old = s.store.script(script.id).takeIf { it.exists() }?.readText(); script.label = values.getValue("name"); s.store.atomic(s.store.script(script.id), values.getValue("script")); try { if (s.state.current?.scriptId == script.id) s.apply(); if (script !in s.state.scripts) s.state.scripts.add(script); s.save() } catch (e: Exception) { if (old != null) s.store.atomic(s.store.script(script.id), old); throw e } }
        }
        "域名策略" -> { val parts = raw.split(" => ", limit = 2); ui.edit("域名策略", listOf(Field("domain", "域名", parts.first()), Field("server", "DNS 服务器", parts.getOrElse(1) { "system://" }))) { values -> require(values.getValue("domain").isNotBlank() && values.getValue("server").isNotBlank()); commit("${values.getValue("domain").trim()} => ${values.getValue("server").trim()}") } }
        "附加规则", "规则列表" -> {
            val parts = raw.split(','); val type = parts.first().ifBlank { "DOMAIN-SUFFIX" }
            if (type in listOf("AND", "OR", "NOT", "SUB-RULE")) { ui.edit("编辑规则", listOf(Field("raw", "规则", raw, multiline = true))) { commit(it.getValue("raw")) }; return }
            var target = parts.lastIndex; while (target > 0 && parts[target] in listOf("src", "no-resolve")) target--
            ui.edit("编辑规则", listOf(Field("type", "规则类型", type, listOf("DOMAIN", "DOMAIN-SUFFIX", "DOMAIN-KEYWORD", "GEOSITE", "GEOIP", "IP-CIDR", "IP-CIDR6", "RULE-SET", "PROCESS-NAME", "PROCESS-PATH", "MATCH")), Field("content", "内容", if (parts.size > 1 && type != "MATCH") parts[1] else ""), Field("target", "分流策略", if (target > 0) parts[target] else "DIRECT", (listOf("DIRECT", "REJECT", "MATCH") + ui.session.groups).distinct()), Field("resolve", "不解析主机名", ("no-resolve" in parts).toString(), listOf("false", "true")), Field("src", "匹配来源 IP", ("src" in parts).toString(), listOf("false", "true")))) { values -> val action = values.getValue("type"); require(action == "MATCH" || values.getValue("content").isNotBlank()); val fields = mutableListOf(action); if (action != "MATCH") fields.add(values.getValue("content").trim()); fields.add(values.getValue("target")); if (values["src"] == "true") fields.add("src"); if (values["resolve"] == "true") fields.add("no-resolve"); commit(fields.joinToString(",")) }
        }
        else -> ui.edit(ui.page.title, listOf(Field("value", "内容", raw))) { values -> require(values.getValue("value").isNotBlank()); commit(values.getValue("value").trim()) }
    }
}

fun webDavDialog(ui: UiActions) {
    val s = ui.session; val props = s.state.config.obj("davProps")
    ui.edit("WebDAV", listOf(Field("uri", "服务器地址", props.get("uri").text()), Field("user", "用户名", props.get("user").text()), Field("password", "密码", props.get("password").text(), secret = true), Field("file", "备份文件名", s.state.config.get("nativeDavFileName").text("FClash-native.zip")), Field("action", "操作", "保存", listOf("保存", "上传备份", "下载并恢复")))) { values ->
        require(URL(values.getValue("uri")).protocol in listOf("http", "https")); require(values.getValue("file").matches(Regex("[A-Za-z0-9_.-]+")))
        props.addProperty("uri", values["uri"]); props.addProperty("user", values["user"]); props.addProperty("password", values["password"]); s.state.config.addProperty("nativeDavFileName", values["file"]); s.save()
        val action = values["action"]
        if (action == "保存") return@edit
        ui.confirm(if (action == "上传备份") "将上传并覆盖服务器上的同名备份。" else "将下载并恢复备份，当前代理会停止。") {
            check(!s.safe) { "安全模式禁止 WebDAV 网络访问" }
            val file = File(s.context.cacheDir, "dav-${System.nanoTime()}.zip")
            try {
                if (action == "上传备份") withContext(Dispatchers.IO) { file.outputStream().use { Archive.backup(s.store.directory, it) } }
                withContext(Dispatchers.IO) {
                    val connection = URL(values.getValue("uri").trimEnd('/') + "/" + values.getValue("file")).openConnection() as HttpURLConnection
                    connection.connectTimeout = 15000; connection.readTimeout = 30000; connection.instanceFollowRedirects = false
                    connection.setRequestProperty("Authorization", "Basic " + android.util.Base64.encodeToString((values.getValue("user") + ":" + values.getValue("password")).toByteArray(), android.util.Base64.NO_WRAP))
                    try {
                        if (action == "上传备份") { connection.requestMethod = "PUT"; connection.doOutput = true; connection.setFixedLengthStreamingMode(file.length()); connection.setRequestProperty("Content-Type", "application/zip"); connection.outputStream.use { output -> file.inputStream().use { it.copyTo(output) } } }
                        check(connection.responseCode in 200..299) { "WebDAV HTTP ${connection.responseCode}" }
                        if (action != "上传备份") connection.inputStream.use { input -> file.outputStream().use { output -> val buffer = ByteArray(8192); var total = 0L; while (true) { val size = input.read(buffer); if (size < 0) break; total += size; require(total <= 128L * 1024 * 1024); output.write(buffer, 0, size) } } }
                    } finally { connection.disconnect() }
                }
                if (action != "上传备份") restore(s, android.net.Uri.fromFile(file))
                ui.message = "操作完成"
            } finally { file.delete() }
        }
    }
}
