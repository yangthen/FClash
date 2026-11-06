package com.follow.fclash.nativeapp

import android.content.Context
import android.net.VpnService
import androidx.compose.runtime.*
import com.follow.fclash.BuildConfig
import com.follow.fclash.Service
import com.follow.fclash.domain.*
import com.follow.clash.common.AccessControlMode
import com.follow.clash.service.models.AccessControlProps
import com.follow.clash.service.models.NotificationParams
import com.follow.clash.service.models.VpnOptions
import com.google.gson.*
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.Semaphore
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.sync.withPermit
import java.io.File
import java.net.HttpURLConnection
import java.net.URL
import java.time.Instant
import java.util.UUID
import kotlin.coroutines.resume
import kotlin.coroutines.resumeWithException

class NativeSession(val context: Context) {
    val safe = BuildConfig.SAFE_MODE
    val store = NativeStore(context, safe)
    var state by mutableStateOf(ClientState(), referentialEqualityPolicy()); private set
    val changes = MutableStateFlow(0)
    var loaded by mutableStateOf(false); private set
    var running by mutableStateOf(false); private set
    var busy by mutableStateOf(false)
    var testing by mutableStateOf(false); private set
    var proxies by mutableStateOf(JsonObject(), referentialEqualityPolicy()); private set
    var groups by mutableStateOf(listOf<String>()); private set
    val delays = mutableStateMapOf<String, Int>()
    val logs = mutableStateListOf<String>()
    val requests = mutableStateListOf<String>()
    var connections by mutableStateOf(JsonArray(), referentialEqualityPolicy()); private set
    var traffic by mutableStateOf(JsonObject(), referentialEqualityPolicy()); private set
    var total by mutableStateOf(JsonObject(), referentialEqualityPolicy()); private set
    var memory by mutableStateOf("0"); private set
    var error by mutableStateOf("")
    private var testGeneration = 0
    private val lifecycle = Mutex()
    private val loadLock = Mutex()
    private val coreInit = Mutex()
    private var attached = false
    private var ready = false
    private var autoUpdates: Job? = null
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    // Persisted models contain mutable JSON/maps. Publish a new holder so that
    // strongly-skipped Compose pages and their captured content lambdas observe
    // edits even when the session identity and underlying collection stay equal.
    fun changed() { state = state.copy(); changes.update { it + 1 } }
    suspend fun load() = loadLock.withLock {
        if (loaded) return@withLock
        withContext(Dispatchers.IO) { state = if (safe) store.load() else store.importLegacy() ?: store.load() }
        loaded = true; changed()
    }
    suspend fun reloadState() { state = withContext(Dispatchers.IO) { store.load() }; ready = false; preview(); changed() }
    suspend fun save() { withContext(Dispatchers.IO) { store.save(state) }; changed() }
    suspend fun attach() {
        load(); attached = true
        if (safe) { preview(); return }
        running = Service.getRunTime() > 0
        initialize()
        Service.setEventListener { raw -> scope.launch { event(raw) } }.getOrThrow()
        if (state.current != null) { if (running) refreshProxies() else apply() }
        autoUpdates?.cancel()
        autoUpdates = scope.launch {
            while (isActive && attached) {
                for (profile in state.profiles.filter { it.autoUpdate && it.url.isNotBlank() }.toList()) {
                    val previous = runCatching { Instant.parse(profile.updatedAt).toEpochMilli() }.getOrDefault(0L)
                    if (System.currentTimeMillis() - previous >= profile.updateInterval.coerceAtLeast(60000)) runCatching { update(profile) }.onFailure(::report)
                }
                delay(60000)
            }
        }
        changed()
    }
    suspend fun detach() {
        attached = false
        autoUpdates?.cancel(); autoUpdates = null
        if (!safe && ready) { runCatching { rpc("stopLog"); rpc("requestEvents", false); Service.setEventListener(null).getOrThrow() } }
    }
    private suspend fun initialize() = coreInit.withLock {
        check(!safe) { "安全模式禁用 Core" }
        Service.bind()
        if (ready && rpc("getIsInit").asBoolean) return@withLock
        withContext(Dispatchers.IO) {
            for (name in listOf("ASN.mmdb", "GEOIP.dat", "GEOIP.metadb", "GEOSITE.dat")) {
                val file = File(store.directory, name)
                if (!file.exists()) context.assets.open(name).use { input -> file.outputStream().use { input.copyTo(it) } }
            }
        }
        check(rpc("initClash", Gson().toJson(mapOf("home-dir" to store.directory.path, "version" to android.os.Build.VERSION.SDK_INT))).asBoolean) { "Core 初始化失败" }
        ready = true
    }
    suspend fun rpc(method: String, data: Any? = null): JsonElement {
        check(!safe) { "安全模式禁用 Core 通信" }
        return withTimeout(30000) {
            suspendCancellableCoroutine { continuation ->
                scope.launch(Dispatchers.IO) {
                    try {
                        Service.invokeAction(Gson().toJson(mapOf("id" to UUID.randomUUID().toString(), "method" to method, "data" to data))) { raw ->
                            try {
                                val response = JsonParser.parseString(raw).asJsonObject
                                check(response.get("code").asInt == 0) { response.get("data").text() }
                                if (continuation.isActive) continuation.resume(response.get("data") ?: JsonNull.INSTANCE)
                            } catch (e: Exception) { if (continuation.isActive) continuation.resumeWithException(e) }
                        }.getOrThrow()
                    } catch (e: Exception) { if (continuation.isActive) continuation.resumeWithException(e) }
                }
            }
        }
    }
    fun decode(value: JsonElement): JsonElement = if (value.isJsonPrimitive && value.asJsonPrimitive.isString) runCatching { JsonParser.parseString(value.asString) }.getOrDefault(value) else value
    suspend fun apply() = lifecycle.withLock { applyInner() }
    private suspend fun applyInner() {
        val profile = state.current ?: return
        val yaml = withContext(Dispatchers.Default) { Configuration.compile(state, profile, store.directory) }
        if (safe) { withContext(Dispatchers.IO) { store.atomic(File(store.directory, "config.yaml"), yaml) }; preview(); save(); return }
        initialize()
        val target = File(store.directory, "config.yaml")
        val previous = withContext(Dispatchers.IO) { target.takeIf(File::exists)?.readText() }
        val candidate = File(store.directory, "candidate.yaml")
        withContext(Dispatchers.IO) { store.atomic(candidate, yaml) }
        check(rpc("validateConfig", candidate.path).text().isEmpty()) { "配置校验失败" }
        val setup = Gson().toJson(mapOf("selected-map" to profile.selected, "test-url" to state.settings.get("testUrl").text("https://www.gstatic.com/generate_204")))
        try {
            withContext(Dispatchers.IO) { store.atomic(target, yaml) }
            val result = rpc("setupConfig", setup).text(); check(result.isEmpty()) { result }
        } catch (error: Exception) {
            withContext(Dispatchers.IO) { if (previous != null) store.atomic(target, previous) else target.delete() }
            if (previous != null) {
                val rollback = runCatching { val result = rpc("setupConfig", setup).text(); check(result.isEmpty()) { result } }
                if (rollback.isFailure) { Service.stopService(); runCatching { rpc("stopListener") }; running = false; changed(); throw IllegalStateException("配置回滚失败，已停止代理", rollback.exceptionOrNull()) }
            }
            throw error
        } finally { candidate.delete() }
        refreshProxies(); save()
    }
    suspend fun start() = lifecycle.withLock { startInner() }
    private suspend fun startInner(applyConfiguration: Boolean = true) {
        check(!safe) { "安全模式不会启动 VPN" }
        if (running && Service.getRunTime() > 0) return
        check(state.current != null) { "请先选择配置" }
        val vpn = state.config.obj("vpnProps")
        val enabled = vpn.flag("enable", true)
        check(!enabled || VpnService.prepare(context) == null) { "请在界面中授权 VPN 后启动" }
        if (applyConfiguration) applyInner()
        val access = vpn.obj("accessControlProps")
        val options = VpnOptions(enabled, state.patch.number("mixed-port", 7890), vpn.flag("ipv6"), vpn.flag("dnsHijacking"),
            AccessControlProps(access.flag("enable"), if (access.get("mode").text("rejectSelected") in listOf("acceptSelected", "ACCEPT_SELECTED")) AccessControlMode.ACCEPT_SELECTED else AccessControlMode.REJECT_SELECTED, access.strings("acceptList"), access.strings("rejectList")),
            vpn.flag("allowBypass", true), vpn.flag("systemProxy", true), state.network.strings("bypassDomain"), state.patch.obj("tun").get("stack").text("mixed"), state.patch.obj("tun").strings("route-address"))
        check(rpc("startListener").asBoolean) { "Core 监听启动失败" }
        try {
            Service.updateNotificationParams(NotificationParams(title = state.current!!.label, stopText = "停止", onlyStatisticsProxy = state.settings.flag("onlyStatisticsProxy"))).getOrThrow()
            running = withTimeout(30000) { Service.startService(options, System.currentTimeMillis()) } > 0
            check(running) { "VPN 服务启动失败" }
        } catch (error: Exception) { rpc("stopListener"); throw error }
        changed()
    }
    suspend fun stop() = lifecycle.withLock { stopInner() }
    private suspend fun stopInner() {
        check(!safe)
        withTimeout(30000) { Service.stopService() }; if (ready) rpc("stopListener"); running = false; changed()
    }
    private fun event(raw: String?) {
        if (raw == null) return
        runCatching {
            val outer = JsonParser.parseString(raw).asJsonObject
            val event = (outer.get("data") as? JsonObject)?.takeIf { it.has("type") } ?: outer
            val data = event.get("data")
            when (event.get("type").text()) {
                "delay" -> if (data is JsonObject) delays[data.get("name").text()] = data.number("value", -1)
                "log", "request" -> {
                    val list = if (event.get("type").text() == "log") logs else requests
                    list.add(data.text()); while (list.size > 500) list.removeAt(0)
                }
            }; changed()
        }
    }
    suspend fun page(name: String) {
        if (safe || !ready || !attached) return
        rpc(if (name == "日志") "startLog" else "stopLog")
        rpc("requestEvents", name == "请求")
    }
    suspend fun poll(page: String) {
        if (!loaded || safe || !ready || !attached) return
        if (page !in listOf("仪表盘", "连接")) return
        when (page) {
            "仪表盘" -> { traffic = decode(rpc("getTraffic", false)).asJsonObject; total = decode(rpc("getTotalTraffic", false)).asJsonObject; memory = rpc("getMemory").text() }
            "连接" -> connections = decode(rpc("getConnections")).asJsonObject.getAsJsonArray("connections") ?: JsonArray()
        }; running = Service.getRunTime() > 0; changed()
    }
    suspend fun refreshProxies() {
        if (safe) { preview(); return }
        val data = decode(rpc("getProxies")).asJsonObject
        proxies = data.getAsJsonObject("proxies") ?: JsonObject(); groups = data.strings("all"); changed()
    }
    private fun preview() {
        val profile = state.current ?: return
        val raw = Configuration.parse(store.profile(profile.id).readText())
        proxies = JsonObject().apply { add("DIRECT", JsonObject().apply { addProperty("type", "Direct") }) }
        (raw.get("proxies") as? JsonArray)?.forEach { proxies.add(it.asJsonObject.get("name").text(), it.deepCopy()) }
        groups = (raw.get("proxy-groups") as? JsonArray)?.map { item ->
            val node = item.asJsonObject.deepCopy(); val name = node.get("name").text()
            node.add("all", node.get("proxies")?.deepCopy() ?: JsonArray()); node.addProperty("now", profile.selected[name]); proxies.add(name, node); name
        } ?: emptyList(); changed()
    }
    suspend fun select(group: String, node: String) {
        val profile = state.current ?: return
        val type = proxies.getAsJsonObject(group)?.get("type").text()
        val next = if (type.lowercase() in listOf("urltest", "url-test", "fallback") && profile.selected[group] == node) "" else node
        if (!safe) { val result = rpc("changeProxy", Gson().toJson(mapOf("group-name" to group, "proxy-name" to next))).text(); check(result.isEmpty()) { result } }
        profile.selected[group] = next; save(); refreshProxies()
    }
    suspend fun test(nodes: List<String>) {
        check(!safe); if (testing) return
        testing = true; changed(); val generation = ++testGeneration; val profile = state.current ?: run { testing = false; return }
        val gate = Semaphore(4)
        try {
            coroutineScope { nodes.distinct().map { name -> async { gate.withPermit {
                if (generation != testGeneration || profile.id != state.currentId) return@withPermit
                runCatching {
                    val result = decode(rpc("asyncTestDelay", Gson().toJson(mapOf("proxy-name" to name, "timeout" to 5000, "test-url" to state.settings.get("testUrl").text("https://www.gstatic.com/generate_204"))))).asJsonObject
                    delays[name] = result.number("value", -1)
                    val country = rpc("getProxyCountry", name).text()
                    if (country.matches(Regex("[A-Z]{2}")) && generation == testGeneration && state.currentId == profile.id) { state.countries[state.countryKey(profile.id, name)] = country; save() }
                }; changed()
            } } }.awaitAll() }
        } finally { testing = false; changed() }
    }
    suspend fun importProfile(label: String, source: String, url: String = ""): Profile {
        withContext(Dispatchers.Default) { Configuration.parse(source) }
        val profile = Profile(label = label.ifBlank { "新配置" }, url = url)
        withContext(Dispatchers.IO) { store.atomic(store.profile(profile.id), source) }
        state.profiles.add(profile); if (state.currentId == null) state.currentId = profile.id
        save(); if (state.currentId == profile.id) apply(); return profile
    }
    suspend fun fetch(url: String): Pair<String, String> = withContext(Dispatchers.IO) {
        check(!safe) { "安全模式禁用网络访问" }
        val uri = URL(url); require(uri.protocol in listOf("https", "http")) { "仅支持 HTTP/HTTPS" }
        val connection = (if (running) uri.openConnection(java.net.Proxy(java.net.Proxy.Type.HTTP, java.net.InetSocketAddress("127.0.0.1", state.patch.number("mixed-port", 7890)))) else uri.openConnection()) as HttpURLConnection
        connection.connectTimeout = 15000; connection.readTimeout = 15000; connection.setRequestProperty("User-Agent", "FClash/${BuildConfig.VERSION_NAME}")
        try {
            check(connection.responseCode in 200..299) { "HTTP ${connection.responseCode}" }
            val bytes = connection.inputStream.use { input ->
                val output = java.io.ByteArrayOutputStream(); val buffer = ByteArray(8192)
                while (true) { val count = input.read(buffer); if (count < 0) break; require(output.size() + count <= 8 * 1024 * 1024) { "响应过大" }; output.write(buffer, 0, count) }; output.toByteArray()
            }
            bytes.toString(Charsets.UTF_8) to (connection.getHeaderField("subscription-userinfo") ?: "")
        } finally { connection.disconnect() }
    }
    suspend fun update(profile: Profile) {
        val (body, info) = fetch(profile.url); withContext(Dispatchers.Default) { Configuration.parse(body) }
        val previous = store.profile(profile.id).readText()
        try { withContext(Dispatchers.IO) { store.atomic(store.profile(profile.id), body) }; if (state.currentId == profile.id) apply() }
        catch (error: Exception) { store.atomic(store.profile(profile.id), previous); throw error }
        profile.updatedAt = Instant.now().toString(); profile.subscriptionInfo = info; save()
    }
    suspend fun changeConfig(change: () -> Unit) = lifecycle.withLock {
        val previous = state.config.deepCopy()
        val wasRunning = running
        try {
            change()
            if (state.current != null) applyInner() else save()
            if (!safe && wasRunning && Configuration.requiresVpnRestart(previous, state.config)) {
                stopInner(); startInner(false)
            }
        } catch (error: Exception) {
            state.config = previous
            runCatching {
                if (state.current != null) applyInner() else save()
                if (!safe && wasRunning && !running) startInner(false)
            }.onFailure { rollback -> report(IllegalStateException("恢复配置失败", rollback)) }
            throw error
        }
    }
    suspend fun switchProfile(profile: Profile) {
        val old = state.currentId; ++testGeneration
        try { state.currentId = profile.id; apply() } catch (error: Exception) { state.currentId = old; throw error }
    }
    fun report(error: Throwable) { if (error is CancellationException) return; this.error = error.message ?: error.toString(); changed() }
}

object NativeRuntime {
    @Volatile private var instance: NativeSession? = null
    fun get(context: Context): NativeSession = instance ?: synchronized(this) { instance ?: NativeSession(context.applicationContext).also { instance = it } }
}
