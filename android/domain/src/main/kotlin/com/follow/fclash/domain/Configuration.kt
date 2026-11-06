package com.follow.fclash.domain

import com.google.gson.*
import com.google.gson.annotations.SerializedName
import org.yaml.snakeyaml.DumperOptions
import org.yaml.snakeyaml.LoaderOptions
import org.yaml.snakeyaml.Yaml
import org.yaml.snakeyaml.constructor.SafeConstructor
import org.mozilla.javascript.Context
import org.mozilla.javascript.ContextFactory
import java.io.File
import java.security.MessageDigest

val json = GsonBuilder().setPrettyPrinting().create()
fun JsonElement?.text(fallback: String = ""): String = if (this == null || isJsonNull) fallback else if (isJsonPrimitive) asString else toString()
fun JsonObject.obj(key: String): JsonObject = (get(key) as? JsonObject) ?: JsonObject().also { add(key, it) }
fun JsonObject.flag(key: String, fallback: Boolean = false): Boolean = get(key)?.takeUnless { it.isJsonNull }?.asBoolean ?: fallback
fun JsonObject.number(key: String, fallback: Int = 0): Int = get(key)?.takeUnless { it.isJsonNull }?.asInt ?: fallback
fun JsonObject.strings(key: String): List<String> = (get(key) as? JsonArray)?.map { it.text() } ?: emptyList()
fun strings(values: Iterable<String>): JsonArray = JsonArray().apply { values.forEach(::add) }

data class Profile(
    @SerializedName("Id") var id: Long = System.currentTimeMillis(),
    @SerializedName("Label") var label: String = "新配置",
    @SerializedName("Url") var url: String = "",
    @SerializedName("UpdatedAt") var updatedAt: String? = null,
    @SerializedName("AutoUpdate") var autoUpdate: Boolean = false,
    @SerializedName("UpdateInterval") var updateInterval: Long = 86400000,
    @SerializedName("OverwriteType") var overwriteType: String = "standard",
    @SerializedName("ScriptId") var scriptId: Long? = null,
    @SerializedName("Selected") var selected: MutableMap<String, String> = linkedMapOf(),
    @SerializedName("AddedRules") var addedRules: MutableList<String> = mutableListOf(),
    @SerializedName("CustomRules") var customRules: MutableList<String> = mutableListOf(),
    @SerializedName("DisabledRules") var disabledRules: MutableList<String> = mutableListOf(),
    @SerializedName("Groups") var groups: JsonArray = JsonArray(),
    @SerializedName("SubscriptionInfo") var subscriptionInfo: String = "",
)
data class Script(@SerializedName("Id") val id: Long = System.currentTimeMillis(), @SerializedName("Label") var label: String = "新脚本")
data class ClientState(
    @SerializedName("FormatVersion") val formatVersion: Int = 1,
    @SerializedName("Config") var config: JsonObject = JsonObject(),
    @SerializedName("Profiles") val profiles: MutableList<Profile> = mutableListOf(),
    @SerializedName("Scripts") val scripts: MutableList<Script> = mutableListOf(),
    @SerializedName("AddedRules") var addedRules: MutableList<String> = mutableListOf(),
    @SerializedName("Countries") val countries: MutableMap<String, String> = linkedMapOf(),
    @SerializedName("ImportedFrom") var importedFrom: String? = null,
) {
    val patch get() = config.obj("patchClashConfig")
    val settings get() = config.obj("appSettingProps")
    val network get() = config.obj("networkProps")
    var currentId: Long?
        get() = config.get("currentProfileId")?.takeUnless { it.isJsonNull }?.asLong
        set(value) { config.addProperty("currentProfileId", value) }
    val current get() = profiles.firstOrNull { it.id == currentId }
    fun countryKey(profile: Long, node: String) = GsonBuilder().disableHtmlEscaping().create().toJson(listOf(profile, node))
}

object Configuration {
    fun requiresVpnRestart(before: JsonObject, after: JsonObject): Boolean {
        fun options(config: JsonObject): JsonObject {
            val patch = config.get("patchClashConfig") as? JsonObject
            return JsonObject().apply {
                add("vpn", config.get("vpnProps"))
                add("bypass", (config.get("networkProps") as? JsonObject)?.get("bypassDomain"))
                add("port", patch?.get("mixed-port"))
                val tun = patch?.get("tun") as? JsonObject
                add("stack", tun?.get("stack")); add("routes", tun?.get("route-address"))
            }
        }
        return options(before) != options(after)
    }
    fun parse(source: String): JsonObject {
        val limits = LoaderOptions().apply { isAllowDuplicateKeys = false; codePointLimit = 8 * 1024 * 1024; maxAliasesForCollections = 30 }
        val value: Any? = Yaml(SafeConstructor(limits)).load(source)
        return json.toJsonTree(value) as? JsonObject ?: error("配置必须是 YAML 映射")
    }
    fun yaml(value: JsonObject): String {
        val options = DumperOptions().apply { defaultFlowStyle = DumperOptions.FlowStyle.BLOCK; isPrettyFlow = true }
        return Yaml(options).dump(yamlValue(value))
    }
    private fun yamlValue(node: JsonElement): Any? = when {
        node.isJsonNull -> null
        node.isJsonObject -> linkedMapOf<String, Any?>().apply { node.asJsonObject.entrySet().forEach { put(it.key, yamlValue(it.value)) } }
        node.isJsonArray -> node.asJsonArray.map(::yamlValue)
        node.asJsonPrimitive.isBoolean -> node.asBoolean
        node.asJsonPrimitive.isNumber -> node.asString.toLongOrNull() ?: node.asDouble
        else -> node.asString
    }
    fun policyMap(value: JsonElement?): JsonObject = when (value) {
        is JsonObject -> value
        is JsonArray -> JsonObject().apply {
            value.forEach { item ->
                val policy = item.asJsonObject
                val server = policy.get("server")
                add(policy.get("domain").text(), if (server is JsonArray) server.deepCopy() else servers(server.text()))
            }
        }
        else -> JsonObject()
    }
    fun defaultDns() = parse("""
        enable: true
        listen: 127.0.0.1:1053
        use-hosts: true
        use-system-hosts: true
        default-nameserver: [223.5.5.5]
        enhanced-mode: fake-ip
        fake-ip-range: 198.18.0.1/16
        fake-ip-filter: ['*.lan', localhost.ptlogin2.qq.com]
        nameserver: [https://doh.pub/dns-query, https://dns.alidns.com/dns-query]
        fallback: [tls://8.8.4.4, tls://1.1.1.1]
        proxy-server-nameserver: [https://doh.pub/dns-query]
        fallback-filter: {geoip: true, geoip-code: CN}
    """.trimIndent())
    fun compile(state: ClientState, profile: Profile, directory: File): String {
        var raw = parse(File(directory, "profiles/${profile.id}.yaml").readText())
        if (profile.overwriteType == "script") {
            val id = profile.scriptId ?: error("此配置缺少脚本")
            raw = runScript(File(directory, "scripts/$id.js").readText(), raw)
        }
        val defaults = parse("mixed-port: 7890\nmode: rule\nallow-lan: false\nlog-level: error\nipv6: false\nunified-delay: true\ntcp-concurrent: true\nfind-process-mode: always\ngeodata-loader: memconservative")
        defaults.entrySet().forEach { (key, value) -> raw.add(key, state.patch.get(key)?.deepCopy() ?: value) }
        state.patch.entrySet().filter { it.key !in listOf("dns", "hosts", "external-controller") }.forEach { raw.add(it.key, it.value.deepCopy()) }
        raw.addProperty("external-controller", state.patch.get("external-controller").text())
        raw.addProperty("external-ui", ""); raw.addProperty("external-ui-url", ""); raw.addProperty("interface-name", "")
        raw.obj("tun").addProperty("enable", false)
        raw.obj("profile").addProperty("store-selected", false)
        val dnsEnabled = (raw.get("dns") as? JsonObject)?.flag("enable") == true
        if (state.config.flag("overrideDns") || !dnsEnabled) {
            val dns = defaultDns()
            (state.patch.get("dns") as? JsonObject)?.entrySet()?.forEach { dns.add(it.key, it.value.deepCopy()) }
            if (!dnsEnabled && "system://" !in dns.strings("nameserver")) dns.add("nameserver", strings(dns.strings("nameserver") + "system://"))
            raw.add("dns", dns)
        }
        val dns = raw.obj("dns")
        if (dns.get("nameserver-policy") is JsonArray) {
            dns.add("nameserver-policy", policyMap(dns.get("nameserver-policy")))
        }
        (dns.get("nameserver-policy") as? JsonObject)?.entrySet()?.toList()?.forEach { (key, value) ->
            if (value.isJsonPrimitive && ',' in value.text()) dns.getAsJsonObject("nameserver-policy").add(key, servers(value.text()))
        }
        if (state.network.flag("appendSystemDns") && "system://" !in dns.strings("nameserver")) dns.add("nameserver", strings(dns.strings("nameserver") + "system://"))
        for (kind in listOf("proxy-providers", "rule-providers")) (raw.get(kind) as? JsonObject)?.entrySet()?.forEach { (_, node) ->
            if (node is JsonObject && node.get("type").text() == "http") {
                val digest = MessageDigest.getInstance("SHA-256").digest(node.get("url").text().toByteArray()).joinToString("") { "%02x".format(it) }
                node.addProperty("path", File(directory, "providers/${profile.id}/$kind/$digest").absolutePath)
            }
        }
        (state.patch.get("hosts") as? JsonObject)?.entrySet()?.forEach { (key, value) -> raw.obj("hosts").add(key, if (value.isJsonArray) value.deepCopy() else servers(value.text())) }
        val baseRules = raw.strings("rules")
        val rules = if (profile.overwriteType == "custom") profile.customRules else {
            val target = baseRules.lastOrNull { it.startsWith("MATCH,") }?.split(',')?.getOrNull(1)
            (state.addedRules + profile.addedRules).filterNot { it in profile.disabledRules }.map { replaceTarget(it, target) } + baseRules
        }
        raw.add("rules", strings(rules))
        if (profile.groups.size() > 0) raw.add("proxy-groups", profile.groups.deepCopy())
        return yaml(raw)
    }
    fun replaceTarget(rule: String, target: String?): String {
        val parts = rule.split(',').toMutableList()
        var index = parts.lastIndex
        while (index > 0 && parts[index] in listOf("src", "no-resolve")) index--
        if (index > 0 && parts[index] == "MATCH" && target != null) parts[index] = target
        return parts.joinToString(",")
    }
    private fun servers(value: String) = strings(value.split(',', '\n').map(String::trim).filter(String::isNotEmpty))
    fun runScript(script: String, config: JsonObject): JsonObject {
        require(script.length < 1024 * 1024) { "脚本过大" }
        val deadline = System.nanoTime() + 3_000_000_000L
        var instructions = 0
        val factory = object : ContextFactory() {
            override fun makeContext(): Context = super.makeContext().apply { optimizationLevel = -1; instructionObserverThreshold = 10000; setClassShutter { false } }
            override fun observeInstructionCount(cx: Context, count: Int) {
                instructions += count
                check(instructions < 1_000_000 && System.nanoTime() < deadline) { "脚本执行超时" }
            }
        }
        return factory.call { cx ->
            val scope = cx.initSafeStandardObjects()
            cx.evaluateString(scope, script, "user.js", 1, null)
            val result = Context.toString(cx.evaluateString(scope, "JSON.stringify(main(JSON.parse(${Gson().toJson(config.toString())})))", "main", 1, null))
            JsonParser.parseString(result) as? JsonObject ?: error("main 必须返回配置对象")
        }
    }
}
