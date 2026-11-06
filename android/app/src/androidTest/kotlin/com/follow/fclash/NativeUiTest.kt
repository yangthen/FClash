package com.follow.fclash

import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createEmptyComposeRule
import androidx.test.core.app.ActivityScenario
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.follow.fclash.domain.*
import com.follow.fclash.nativeapp.NativeStore
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class NativeUiTest {
    @get:Rule val compose = createEmptyComposeRule()
    @Test fun legacySqliteImportPreservesSourceAndOrder() {
        check(BuildConfig.SAFE_MODE)
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val root = java.io.File(context.cacheDir, "legacy-test-${System.nanoTime()}").apply { mkdirs() }
        val legacy = java.io.File(root, "legacy").apply { mkdirs() }
        try {
            val database = java.io.File(legacy, "database.sqlite")
            android.database.sqlite.SQLiteDatabase.openOrCreateDatabase(database, null).use { db ->
                db.execSQL("CREATE TABLE profiles (id INTEGER, label TEXT, url TEXT, overwrite_type TEXT, script_id INTEGER, auto_update INTEGER, auto_update_duration_millis INTEGER, subscription_info TEXT, last_update_date INTEGER, selected_map TEXT, \"order\" INTEGER)")
                db.execSQL("INSERT INTO profiles VALUES (5, '旧配置', '', 'standard', NULL, 1, 3600000, NULL, 1700000000, '{\"代理\":\"节点\"}', 0)")
                db.execSQL("CREATE TABLE rules (id INTEGER, rule_action TEXT, content TEXT, rule_target TEXT, rule_provider TEXT, sub_rule TEXT, src INTEGER, no_resolve INTEGER)")
                db.execSQL("INSERT INTO rules VALUES (1,'DOMAIN_SUFFIX','dev.sijingkeji.cn','DIRECT',NULL,NULL,0,0), (2,'GEOIP','CN','DIRECT',NULL,NULL,0,1)")
                db.execSQL("CREATE TABLE profile_rule_mapping (rule_id INTEGER, profile_id INTEGER, scene TEXT, \"order\" TEXT)")
                db.execSQL("INSERT INTO profile_rule_mapping VALUES (2,NULL,NULL,'b'), (1,NULL,NULL,'a'), (2,5,'disabled','c')")
                db.execSQL("CREATE TABLE proxy_groups (id INTEGER, profile_id INTEGER, name TEXT, type TEXT, proxies TEXT, disable_u_d_p INTEGER, \"order\" TEXT)")
                db.execSQL("INSERT INTO proxy_groups VALUES (1,5,'代理','select','[\"DIRECT\"]',1,'a')")
                db.execSQL("CREATE TABLE scripts (id INTEGER, label TEXT)")
                db.execSQL("INSERT INTO scripts VALUES (7,'旧脚本')")
            }
            java.io.File(legacy, "profiles").mkdirs()
            java.io.File(legacy, "profiles/5.yaml").writeText("rules: ['MATCH,DIRECT']")
            java.io.File(legacy, "scripts").mkdirs()
            java.io.File(legacy, "scripts/7.js").writeText("function main(c) { return c; }")
            java.io.File(legacy, "config.json").writeText("{\"version\":3,\"currentProfileId\":5}")
            val original = database.readBytes()
            val store = NativeStore(context, true, java.io.File(root, "native"))
            val imported = requireNotNull(store.importLegacy(legacy))
            assertEquals(5L, imported.currentId)
            assertEquals(listOf("DOMAIN-SUFFIX,dev.sijingkeji.cn,DIRECT", "GEOIP,CN,DIRECT,no-resolve"), imported.addedRules)
            assertEquals(listOf("GEOIP,CN,DIRECT,no-resolve"), imported.current!!.disabledRules)
            assertEquals("节点", imported.current!!.selected["代理"])
            assertTrue(imported.current!!.groups[0].asJsonObject.get("disable-udp").asBoolean)
            assertEquals("function main(c) { return c; }", store.script(7).readText())
            assertEquals("DOMAIN-SUFFIX,dev.sijingkeji.cn,DIRECT", Configuration.parse(Configuration.compile(imported, imported.current!!, store.directory)).strings("rules")[0])
            assertArrayEquals(original, database.readBytes())
            assertNull(store.importLegacy(legacy))
        } finally { root.deleteRecursively() }
    }
    @Test fun interpretedScriptsAndYamlOnAndroid() {
        check(BuildConfig.SAFE_MODE)
        val input = Configuration.parse("mixed-port: 7890\nipv6: false\nrules: ['MATCH,DIRECT']")
        val result = Configuration.runScript("function main(config) { config['mixed-port'] = 7891; config.rules.unshift('GEOSITE,cn,DIRECT'); return config; }", input)
        val roundTrip = Configuration.parse(Configuration.yaml(result))
        assertEquals(7891, roundTrip.get("mixed-port").asInt)
        assertFalse(roundTrip.get("ipv6").asBoolean)
        assertEquals("GEOSITE,cn,DIRECT", roundTrip.strings("rules")[0])
        assertTrue(runCatching { Configuration.runScript("function main(c) { while(true) {} }", input) }.isFailure)
        assertTrue(runCatching { Configuration.runScript("function main(c) { c.secret = java.lang.System.getProperty('user.home'); return c; }", input) }.isFailure)
    }
    @Test fun originalNavigationDnsRulesAndFlags() {
        check(BuildConfig.SAFE_MODE) { "UI tests must only run against the isolated safe-mode app" }
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val store = NativeStore(context, true)
        val state = ClientState(); val profile = Profile(id = 1, label = "测试配置")
        state.profiles.add(profile); state.currentId = profile.id; state.settings.addProperty("locale", "zh_CN")
        state.config.addProperty("overrideDns", true)
        state.patch.add("dns", Configuration.parse("nameserver: [system://, 223.5.5.5]\nnameserver-policy:\n  '+.dev.sijingkeji.cn': system://\n  'geosite:cn': 223.5.5.5"))
        state.addedRules.addAll(listOf("DOMAIN-SUFFIX,dev.sijingkeji.cn,DIRECT", "GEOSITE,cn,DIRECT"))
        state.countries[state.countryKey(1, "测试节点")] = "JP"
        store.atomic(store.profile(1), "proxies:\n  - {name: 测试节点, type: ss, server: example.invalid, port: 443, cipher: aes-128-gcm, password: test}\nproxy-groups:\n  - {name: 代理选择, type: select, proxies: [测试节点, DIRECT]}\n  - {name: 备用分组, type: select, proxies: [测试节点]}\nrules: ['MATCH,代理选择']")
        store.save(state)
        ActivityScenario.launch(MainActivity::class.java).use {
            compose.waitUntil(15000) { compose.onAllNodesWithText("网络速度").fetchSemanticsNodes().isNotEmpty() }
            compose.onNodeWithText("网络速度").assertIsDisplayed()
            compose.onNodeWithText("代理", useUnmergedTree = false).performClick()
            compose.onNodeWithText("测试节点").assertExists()
            compose.onNodeWithText("🇯🇵").assertExists()
            compose.onNodeWithContentDescription("设置").performClick()
            compose.onNodeWithText("列表").performScrollTo().performClick()
            compose.onNodeWithText("保存").performClick()
            compose.waitUntil(10000) { store.load().config.obj("proxiesStyleProps").get("type").text() == "list" }
            compose.onNodeWithText("备用分组").assertIsDisplayed()
            compose.onNodeWithTag("proxy-grid").performScrollToNode(hasTestTag("proxy/备用分组/测试节点"))
            compose.onNodeWithTag("proxy/备用分组/测试节点").performClick()
            compose.waitUntil(10000) { store.load().current!!.selected["备用分组"] == "测试节点" }
            compose.onNodeWithText("工具").performClick()
            compose.onNodeWithText("进阶配置").performScrollTo().performClick()
            compose.onNodeWithText("DNS").performClick()
            compose.onNodeWithText("域名策略").performScrollTo().performClick()
            compose.onNodeWithText("+.dev.sijingkeji.cn").assertIsDisplayed()
            compose.onNodeWithText("system://").assertIsDisplayed()
            compose.onNodeWithText("+.dev.sijingkeji.cn").performClick()
            compose.onNodeWithText("取消").performClick()
            compose.onNodeWithContentDescription("返回").performClick()
            compose.onNodeWithText("覆盖 DNS").assertExists()
            compose.onNodeWithText("域名策略").assertIsDisplayed()
            compose.onNodeWithContentDescription("返回").performClick()
            compose.onNodeWithText("附加规则").performClick()
            compose.onNodeWithText("DOMAIN_SUFFIX").assertIsDisplayed()
            compose.onNodeWithText("GEOSITE").assertIsDisplayed()
            compose.onAllNodesWithContentDescription("上移")[1].performClick()
            compose.waitUntil(10000) { store.load().addedRules.firstOrNull() == "GEOSITE,cn,DIRECT" }
            assertEquals("GEOSITE,cn,DIRECT", Configuration.parse(java.io.File(store.directory, "config.yaml").readText()).strings("rules")[0])
        }
        assertEquals(listOf("+.dev.sijingkeji.cn", "geosite:cn"), store.load().patch.obj("dns").obj("nameserver-policy").keySet().toList())
    }
}
