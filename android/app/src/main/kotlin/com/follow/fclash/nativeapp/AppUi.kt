@file:OptIn(androidx.compose.material3.ExperimentalMaterial3Api::class)
package com.follow.fclash.nativeapp

import android.content.Intent
import android.net.Uri
import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.*
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveableStateHolder
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.toArgb
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.repeatOnLifecycle
import com.follow.fclash.BuildConfig
import com.follow.fclash.domain.*
import com.google.gson.*
import kotlinx.coroutines.*
import java.io.File
import java.util.Locale

data class Page(val title: String, val kind: String = title, val key: String = "", val profileId: Long? = null)
data class Field(val key: String, val title: String, val value: String, val choices: List<String> = emptyList(), val multiline: Boolean = false, val secret: Boolean = false)
data class Editor(val title: String, val fields: List<Field>, val save: suspend (Map<String, String>) -> Unit)
class UiActions(val session: NativeSession, private val scope: CoroutineScope, val requestLocation: () -> Unit = {}) {
    val path = mutableStateListOf(Page("仪表盘"))
    var editor by mutableStateOf<Editor?>(null)
    var confirmation by mutableStateOf<Pair<String, suspend () -> Unit>?>(null)
    var message by mutableStateOf<String?>(null)
    var searchVisible by mutableStateOf(false)
    var dashboardEditing by mutableStateOf(false)
    var visibleProxyGroup by mutableStateOf("")
    val page get() = path.last()
    fun root(name: String) { path.clear(); path.add(Page(name)) }
    fun push(name: String, kind: String = name, key: String = "", profile: Long? = null) { path.add(Page(name, kind, key, profile)) }
    fun run(action: suspend () -> Unit) { scope.launch { try { action() } catch (e: Exception) { session.report(e) } } }
    fun edit(title: String, fields: List<Field>, save: suspend (Map<String, String>) -> Unit) { editor = Editor(title, fields, save) }
    fun confirm(text: String, action: suspend () -> Unit) { confirmation = text to action }
}
private val LocalWords = staticCompositionLocalOf<Map<String, String>> { emptyMap() }
@Composable fun T(text: String): String {
    val label = mapOf("system" to "默认", "light" to "浅色", "dark" to "深色", "none" to "默认", "delay" to "延迟", "name" to "名称", "tight" to "紧凑", "standard" to "标准", "loose" to "宽松", "expand" to "展开", "shrink" to "紧凑", "true" to "开启", "false" to "关闭", "script" to "脚本", "custom" to "自定义", "acceptSelected" to "仅允许选中应用", "rejectSelected" to "绕过选中应用")[text] ?: text
    val display = when (text) { "tab" -> "标签页"; "list" -> "列表"; else -> label }
    return LocalWords.current[display] ?: display
}

@Composable
fun FClashApp(session: NativeSession, requestStart: () -> Unit, requestLocation: () -> Unit, incoming: String?, consumed: () -> Unit) {
    val revision by session.changes.collectAsStateWithLifecycle()
    @Suppress("UNUSED_VARIABLE") val observe = revision
    val scope = rememberCoroutineScope(); val ui = remember { UiActions(session, scope, requestLocation) }
    val savedPages = rememberSaveableStateHolder()
    val owner = LocalLifecycleOwner.current
    val theme = session.state.config.obj("themeProps")
    val dark = when (theme.get("themeMode").text("system")) { "light" -> false; "dark" -> true; else -> isSystemInDarkTheme() }
    val seed = Color(theme.get("primaryColor")?.asLong ?: 0xffd8c0c3)
    val colors = remember(dark, seed) { materialPalette(session, seed, dark) }
    val activity = androidx.activity.compose.LocalActivity.current
    SideEffect {
        activity?.let { androidx.core.view.WindowCompat.getInsetsController(it.window, it.window.decorView).apply { isAppearanceLightStatusBars = !dark; isAppearanceLightNavigationBars = !dark } }
    }
    val words = remember(session.state.settings.get("locale").text()) { translations(session) }
    LaunchedEffect(ui.page, session.loaded) {
        owner.lifecycle.repeatOnLifecycle(Lifecycle.State.STARTED) {
            try { session.page(ui.page.title) } catch (e: Exception) { session.report(e) }
            while (isActive) { delay(1000); try { session.poll(ui.page.title) } catch (e: Exception) { session.report(e) } }
        }
    }
    LaunchedEffect(incoming, session.loaded) {
        if (!incoming.isNullOrBlank() && session.loaded) {
            runCatching {
                val uri = Uri.parse(incoming)
                require(uri.scheme in listOf("fclash", "flclash", "clash", "clashmeta") && uri.host == "install-config")
                val url = uri.getQueryParameter("url") ?: error("订阅 URL 为空")
                subscriptionDialog(ui, uri.getQueryParameter("name") ?: "新配置", url)
            }.onFailure(session::report)
            consumed()
        }
    }
    BackHandler(ui.path.size > 1) { ui.path.removeAt(ui.path.lastIndex) }
    CompositionLocalProvider(LocalWords provides words) {
        MaterialTheme(colorScheme = colors, shapes = Shapes(medium = RoundedCornerShape(14.dp), large = RoundedCornerShape(14.dp))) {
            val importFile = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { uri -> if (uri != null) ui.run {
                val body = withContext(Dispatchers.IO) { session.context.contentResolver.openInputStream(uri)!!.bufferedReader().use { it.readText() } }
                ui.edit("添加配置", listOf(Field("label", "名称", "新配置"))) { values -> session.importProfile(values.getValue("label"), body) }
            } }
            val exportBackup = rememberLauncherForActivityResult(ActivityResultContracts.CreateDocument("application/zip")) { uri -> if (uri != null) ui.run {
                session.save(); withContext(Dispatchers.IO) { session.context.contentResolver.openOutputStream(uri)!!.use { Archive.backup(session.store.directory, it) } }
                ui.message = "备份已导出"
            } }
            val restoreBackup = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { uri -> if (uri != null) ui.confirm("恢复前会保存当前配置，恢复后代理将停止。") {
                restore(session, uri); ui.root("工具"); ui.message = "恢复完成"
            } }
            Scaffold(
                topBar = { TopAppBar(title = { Text(T(ui.page.title)) }, navigationIcon = { if (ui.path.size > 1) IconButton(onClick = { ui.path.removeAt(ui.path.lastIndex) }) { Icon(Icons.Default.ArrowBack, T("返回")) } }, actions = {
                    if (session.safe) Text(T("安全模式"), style = MaterialTheme.typography.labelSmall)
                    when (ui.page.kind) {
                        "仪表盘" -> {
                            if (ui.dashboardEditing) IconButton(onClick = { ui.edit("添加", listOf(Field("key", "卡片", "memoryInfo", listOf("networkSpeed", "systemProxyButton", "tunButton", "outboundMode", "networkDetection", "trafficUsage", "intranetIp", "memoryInfo")))) { values -> val settings = session.state.settings; val order = if (settings.has("dashboardWidgets")) settings.strings("dashboardWidgets") else listOf("networkSpeed", "systemProxyButton", "tunButton", "outboundMode", "networkDetection", "trafficUsage", "intranetIp"); settings.add("dashboardWidgets", strings((order + values.getValue("key")).distinct())); session.save() } }) { Icon(Icons.Default.Add, T("添加")) }
                            IconButton(onClick = { ui.dashboardEditing = !ui.dashboardEditing }) { Icon(if (ui.dashboardEditing) Icons.Default.Check else Icons.Default.Edit, T(if (ui.dashboardEditing) "保存" else "编辑")) }
                        }
                        "配置" -> {
                            IconButton(onClick = { ui.run { session.state.profiles.filter { it.url.isNotBlank() }.forEach { session.update(it) } } }) { Icon(Icons.Default.Sync, T("更新全部")) }
                            IconButton(onClick = { ui.push("配置排序", "配置排序") }) { Icon(Icons.Default.Sort, T("排序")) }
                        }
                        "代理" -> { IconButton(onClick = { ui.searchVisible = !ui.searchVisible }) { Icon(Icons.Default.Search, T("搜索")) }; IconButton(onClick = { proxyOptions(ui) }) { Icon(Icons.Default.MoreVert, T("设置")) } }
                        "附加规则", "规则列表", "DNS列表", "域名策略", "脚本" -> IconButton(onClick = { addListEntry(ui) }) { Icon(Icons.Default.Add, T("添加")) }
                        "日志", "请求" -> IconButton(onClick = { (if (ui.page.kind == "日志") session.logs else session.requests).clear(); session.changed() }) { Icon(Icons.Default.DeleteSweep, T("清空")) }
                    }
                }) },
                bottomBar = { if (ui.path.size == 1) NavigationBar { listOf("仪表盘" to Icons.Default.SpaceDashboard, "代理" to Icons.Default.Article, "配置" to Icons.Default.Folder, "工具" to Icons.Default.Construction).filter { it.first != "代理" || session.groups.isNotEmpty() }.forEach { (title, icon) ->
                    NavigationBarItem(selected = ui.page.title == title, onClick = { ui.root(title) }, icon = { Icon(icon, T(title)) }, label = { Text(T(title)) })
                } } },
                floatingActionButton = {
                    when (ui.page.kind) {
                        "仪表盘" -> FloatingActionButton(onClick = { if (session.running) ui.run { session.stop() } else requestStart() }) { Icon(if (session.running) Icons.Default.Stop else Icons.Default.PlayArrow, T(if (session.running) "停止" else "启动")) }
                        "配置" -> FloatingActionButton(onClick = { ui.edit("添加配置", listOf(Field("type", "导入方式", "URL", listOf("URL", "文件")))) { if (it["type"] == "文件") importFile.launch(arrayOf("*/*")) else subscriptionDialog(ui) } }) { Icon(Icons.Default.Add, T("添加配置")) }
                        "代理" -> FloatingActionButton(onClick = { ui.run { session.test(currentNodes(session, activeProxyGroup(ui))) } }) { if (session.testing) CircularProgressIndicator(Modifier.size(24.dp), strokeWidth = 2.dp) else Icon(Icons.Default.Speed, T("延迟测试")) }
                    }
                },
            ) { padding ->
                Box(Modifier.fillMaxSize().padding(padding)) {
                    savedPages.SaveableStateProvider("${ui.page.kind}/${ui.page.key}/${ui.page.profileId}") {
                    when (ui.page.kind) {
                        "仪表盘" -> Dashboard(ui)
                        "代理" -> Proxies(ui)
                        "配置" -> Profiles(ui)
                        "工具" -> Tools(ui)
                        "进阶配置" -> Advanced(ui)
                        "基本配置", "网络", "应用", "主题", "DNS", "VPN", "访问控制", "覆写" -> Settings(ui)
                        "附加规则", "规则列表", "DNS列表", "域名策略", "配置排序", "脚本", "按需连接", "仪表盘排序" -> OrderedPage(ui)
                        "日志", "请求" -> History(ui)
                        "连接" -> Connections(ui)
                        "资源" -> Resources(ui)
                        "备份与恢复" -> SettingsList {
                            Section("远程"); Setting("WebDAV", session.state.config.obj("davProps").get("uri").text()) { webDavDialog(ui) }
                            Section("本地"); Setting("备份") { exportBackup.launch("FClash-${System.currentTimeMillis()}.zip") }; Setting("恢复") { restoreBackup.launch(arrayOf("application/zip", "application/octet-stream")) }
                        }
                        "关于" -> About(ui)
                    }
                    }
                    if (!session.loaded) CircularProgressIndicator(Modifier.align(androidx.compose.ui.Alignment.Center))
                }
            }
            EditorDialog(ui)
            ui.confirmation?.let { (text, action) -> AlertDialog(onDismissRequest = { ui.confirmation = null }, title = { Text(T("确认")) }, text = { Text(T(text)) }, confirmButton = { TextButton(onClick = { ui.confirmation = null; ui.run(action) }) { Text(T("确定")) } }, dismissButton = { TextButton(onClick = { ui.confirmation = null }) { Text(T("取消")) } }) }
            val message = session.error.takeIf { it.isNotBlank() } ?: ui.message
            if (message != null) AlertDialog(onDismissRequest = { session.error = ""; ui.message = null; session.changed() }, title = { Text(T("提示")) }, text = { Text(T(message)) }, confirmButton = { TextButton(onClick = { session.error = ""; ui.message = null; session.changed() }) { Text(T("关闭")) } })
        }
    }
}

@Composable private fun EditorDialog(ui: UiActions) {
    val editor = ui.editor ?: return
    val values = remember(editor) { mutableStateMapOf<String, String>().apply { editor.fields.forEach { put(it.key, it.value) } } }
    AlertDialog(onDismissRequest = { ui.editor = null }, title = { Text(T(editor.title)) }, text = {
        Column(Modifier.verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            editor.fields.forEach { field ->
                if (field.choices.isEmpty()) OutlinedTextField(value = values[field.key] ?: "", onValueChange = { values[field.key] = it }, label = { Text(T(field.title)) }, modifier = Modifier.fillMaxWidth().then(if (field.multiline) Modifier.heightIn(min = 150.dp) else Modifier), minLines = if (field.multiline) 5 else 1, visualTransformation = if (field.secret) androidx.compose.ui.text.input.PasswordVisualTransformation() else androidx.compose.ui.text.input.VisualTransformation.None)
                else { Text(T(field.title), style = MaterialTheme.typography.labelMedium); field.choices.forEach { choice -> Row(Modifier.fillMaxWidth().clickable { values[field.key] = choice }, verticalAlignment = androidx.compose.ui.Alignment.CenterVertically) { RadioButton(selected = values[field.key] == choice, onClick = { values[field.key] = choice }); Text(T(choice)) } } }
            }
        }
    }, confirmButton = { TextButton(onClick = { ui.editor = null; ui.run { editor.save(values.toMap()) } }) { Text(T("保存")) } }, dismissButton = { TextButton(onClick = { ui.editor = null }) { Text(T("取消")) } })
}
@Composable fun SettingsList(content: @Composable ColumnScope.() -> Unit) { Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(bottom = 88.dp), content = content) }
@Composable fun Section(title: String) { Text(T(title), Modifier.padding(start = 16.dp, top = 24.dp, bottom = 8.dp), color = MaterialTheme.colorScheme.primary, style = MaterialTheme.typography.labelLarge) }
@Composable fun Setting(title: String, subtitle: String = "", icon: ImageVector? = null, trailing: (@Composable () -> Unit)? = null, click: (() -> Unit)? = null) {
    ListItem(headlineContent = { Text(T(title)) }, supportingContent = if (subtitle.isBlank()) null else ({ Text(T(subtitle)) }), leadingContent = if (icon == null) null else ({ Icon(icon, null) }), trailingContent = trailing, modifier = if (click == null) Modifier else Modifier.clickable(onClick = click), colors = ListItemDefaults.colors(containerColor = Color.Transparent))
}
fun subscriptionDialog(ui: UiActions, label: String = "新配置", url: String = "") { ui.edit("从 URL 导入", listOf(Field("name", "名称", label), Field("url", "URL", url))) { values -> val (body, info) = ui.session.fetch(values.getValue("url")); val p = ui.session.importProfile(values.getValue("name"), body, values.getValue("url")); p.subscriptionInfo = info; ui.session.save() } }
private fun translations(session: NativeSession): Map<String, String> = runCatching {
    val language = session.state.settings.get("locale").text(Locale.getDefault().language).split('_', '-')[0]
    if (language == "zh") emptyMap() else {
        val source = session.context.assets.open("intl_zh_CN.json").bufferedReader().use { JsonParser.parseReader(it).asJsonObject }
        val destination = session.context.assets.open("intl_${if (language in listOf("en", "ja", "ru")) language else "en"}.json").bufferedReader().use { JsonParser.parseReader(it).asJsonObject }
        source.entrySet().filter { !it.key.startsWith('@') && it.value.isJsonPrimitive }.associate { it.value.text() to destination.get(it.key).text(it.value.text()) }
    }
}.getOrDefault(emptyMap())
private fun materialPalette(session: NativeSession, seed: Color, dark: Boolean): ColorScheme = runCatching {
    val colors = session.context.assets.open("material-palettes.json").bufferedReader().use { JsonParser.parseReader(it).asJsonObject }
    val key = "%08x".format(seed.toArgb())
    val palette = (colors.get(key) ?: colors.get("ffd8c0c3")).asJsonObject.getAsJsonObject(if (dark) "dark" else "light")
    fun color(name: String) = Color(android.graphics.Color.parseColor(palette.get(name).text()))
    (if (dark) darkColorScheme() else lightColorScheme()).copy(primary = color("PrimaryBrush"), onPrimary = color("onPrimary"), surface = color("CanvasBrush"), onSurface = color("onSurface"), surfaceContainer = color("RailBrush"), surfaceContainerLow = color("CardBrush"), surfaceContainerHighest = color("CardStroke"), onSurfaceVariant = color("MutedBrush"), secondaryContainer = color("SelectionBrush"), onSecondaryContainer = color("onSurface"), primaryContainer = color("SelectionBrush"), onPrimaryContainer = color("onSurface"), secondary = color("PrimaryBrush"), onSecondary = color("onPrimary"), background = color("CanvasBrush"))
}.getOrElse { if (dark) darkColorScheme() else lightColorScheme() }
suspend fun restore(session: NativeSession, uri: Uri) {
    val stage = File(session.context.cacheDir, "restore-${System.nanoTime()}")
    try {
        withContext(Dispatchers.IO) { session.context.contentResolver.openInputStream(uri)!!.use { Archive.unpack(it, stage) } }
        val source = if (File(stage, "native-state.json").exists()) stage else {
            val legacyRoot = stage.walkTopDown().firstOrNull { it.name == "database.sqlite" }?.parentFile ?: error("备份中没有配置数据")
            val converted = File(session.context.cacheDir, "converted-${System.nanoTime()}")
            NativeStore(session.context, true, converted).importLegacy(legacyRoot) ?: error("旧版备份导入失败")
            converted
        }
        val restored = json.fromJson(File(source, "native-state.json").readText(), ClientState::class.java)
        require(restored.formatVersion == 1)
        restored.profiles.forEach { Configuration.parse(File(source, "profiles/${it.id}.yaml").readText()) }
        if (session.running) session.stop()
        withContext(Dispatchers.IO) {
            session.store.save(session.state)
            File(session.context.filesDir, "before-restore-${System.currentTimeMillis()}.zip").outputStream().use { Archive.backup(session.store.directory, it) }
            for (name in listOf("profiles", "scripts")) File(source, name).takeIf { it.isDirectory }?.copyRecursively(File(session.store.directory, name), overwrite = true)
            session.store.atomic(session.store.stateFile, json.toJson(restored))
        }
        session.reloadState()
        if (source != stage) withContext(Dispatchers.IO) { source.deleteRecursively() }
    } finally { withContext(Dispatchers.IO) { stage.deleteRecursively() } }
}
