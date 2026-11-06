package com.follow.fclash.nativeapp

import android.content.Context
import android.database.sqlite.SQLiteDatabase
import android.util.AtomicFile
import com.follow.fclash.domain.*
import com.google.gson.*
import java.io.File

class NativeStore(private val context: Context, safe: Boolean, target: File? = null) {
    val directory = target ?: File(context.filesDir, if (safe) "native-safe" else "native")
    val stateFile get() = File(directory, "native-state.json")
    fun profile(id: Long) = File(directory, "profiles/$id.yaml")
    fun script(id: Long) = File(directory, "scripts/$id.js")
    init { File(directory, "profiles").mkdirs(); File(directory, "scripts").mkdirs() }
    fun load(): ClientState {
        if (stateFile.exists()) return json.fromJson(stateFile.readText(), ClientState::class.java).also { require(it.formatVersion == 1) }
        return ClientState()
    }
    @Synchronized fun save(state: ClientState) = atomic(stateFile, json.toJson(state))
    fun atomic(file: File, content: String) {
        file.parentFile?.mkdirs()
        val atomic = AtomicFile(file); val output = atomic.startWrite()
        try { output.write(content.toByteArray()); atomic.finishWrite(output) } catch (error: Throwable) { atomic.failWrite(output); throw error }
    }
    fun importLegacy(from: File? = null): ClientState? {
        if (stateFile.exists()) return null
        val source = from ?: listOf(context.filesDir, File(context.applicationInfo.dataDir, "app_flutter")).firstOrNull { File(it, "database.sqlite").isFile } ?: return null
        val state = ClientState(importedFrom = source.path)
        val preferences = context.getSharedPreferences("FlutterSharedPreferences", Context.MODE_PRIVATE)
        val archived = File(source, "shared_preferences.json").takeIf(File::exists)?.readText()?.let { JsonParser.parseString(it).asJsonObject }
        val saved = if (from == null) preferences.getString("flutter.config", null)?.let(::JsonPrimitive) else null
        val config = archived?.get("flutter.config") ?: archived?.get("config") ?: saved
            ?: File(source, "config.json").takeIf(File::exists)?.readText()?.let(JsonParser::parseString)
        if (config != null) state.config = if (config.isJsonObject) config.asJsonObject else JsonParser.parseString(config.asString).asJsonObject
        val countries = (archived?.get("flutter.proxyCountries") ?: archived?.get("proxyCountries"))?.let { if (it.isJsonObject) it.asJsonObject else JsonParser.parseString(it.asString).asJsonObject }
        countries?.entrySet()?.forEach { state.countries[it.key] = it.value.text() }
        if (from == null) preferences.getString("flutter.proxyCountries", null)?.let { encoded -> JsonParser.parseString(encoded).asJsonObject.entrySet().forEach { state.countries[it.key] = it.value.text() } }
        SQLiteDatabase.openDatabase(File(source, "database.sqlite").path, null, SQLiteDatabase.OPEN_READONLY).use { database ->
            database.rawQuery("SELECT * FROM profiles ORDER BY \"order\", id", null).use { rows ->
                fun value(key: String) = rows.getColumnIndex(key).let { if (it < 0 || rows.isNull(it)) "" else rows.getString(it) }
                while (rows.moveToNext()) {
                    val p = Profile(id = value("id").toLong(), label = value("label"), url = value("url"), overwriteType = value("overwrite_type").ifEmpty { "standard" }, scriptId = value("script_id").toLongOrNull(), autoUpdate = value("auto_update") == "1", updateInterval = value("auto_update_duration_millis").toLongOrNull() ?: 86400000, subscriptionInfo = value("subscription_info"))
                    p.updatedAt = value("last_update_date").toLongOrNull()?.let { java.time.Instant.ofEpochSecond(it).toString() }
                    if (value("selected_map").isNotEmpty()) JsonParser.parseString(value("selected_map")).asJsonObject.entrySet().forEach { p.selected[it.key] = it.value.text() }
                    state.profiles.add(p)
                }
            }
            database.rawQuery("SELECT r.*, m.profile_id, m.scene FROM rules r JOIN profile_rule_mapping m ON m.rule_id=r.id ORDER BY m.\"order\"", null).use { rows ->
                fun value(key: String) = rows.getColumnIndex(key).let { if (it < 0 || rows.isNull(it)) "" else rows.getString(it) }
                while (rows.moveToNext()) {
                    val action = value("rule_action").replace('_', '-')
                    val parts = mutableListOf(action)
                    if (action != "MATCH") parts.add(value(if (action == "RULE-SET") "rule_provider" else "content"))
                    parts.add(value(if (action == "SUB-RULE") "sub_rule" else "rule_target"))
                    if (value("src") == "1") parts.add("src"); if (value("no_resolve") == "1") parts.add("no-resolve")
                    val profile = state.profiles.firstOrNull { it.id == value("profile_id").toLongOrNull() }
                    val list = if (profile == null) state.addedRules else when (value("scene")) { "custom" -> profile.customRules; "disabled" -> profile.disabledRules; else -> profile.addedRules }
                    list.add(parts.joinToString(","))
                }
            }
            database.rawQuery("SELECT * FROM proxy_groups ORDER BY \"order\"", null).use { rows ->
                while (rows.moveToNext()) {
                    val profile = state.profiles.firstOrNull { it.id == rows.getLong(rows.getColumnIndexOrThrow("profile_id")) } ?: continue
                    val group = JsonObject()
                    rows.columnNames.forEachIndexed { index, key ->
                        if (key !in listOf("id", "profile_id", "order") && !rows.isNull(index)) {
                            val name = key.replace("disable_u_d_p", "disable_udp").replace('_', '-')
                            val value = rows.getString(index)
                            group.add(name, when { key in listOf("proxies", "use") -> JsonParser.parseString(value); key in listOf("lazy", "hidden", "disable_u_d_p", "disable_udp", "include_all", "include_all_proxies", "include_all_providers") -> JsonPrimitive(value == "1"); rows.getType(index) == 1 -> JsonPrimitive(rows.getLong(index)); else -> JsonPrimitive(value) })
                        }
                    }; profile.groups.add(group)
                }
            }
            if (database.rawQuery("SELECT name FROM sqlite_master WHERE type='table' AND name='scripts'", null).use { it.count > 0 }) database.rawQuery("SELECT id, label FROM scripts", null).use { rows -> while (rows.moveToNext()) state.scripts.add(Script(rows.getLong(0), rows.getString(1))) }
        }
        for (name in listOf("profiles", "scripts")) File(source, name).takeIf { it.isDirectory }?.walkTopDown()?.filter { it.isFile }?.forEach { file ->
            val target = File(directory, "$name/${file.relativeTo(File(source, name)).path}")
            target.parentFile?.mkdirs(); file.copyTo(target, overwrite = true)
        }
        save(state)
        return state
    }
}
