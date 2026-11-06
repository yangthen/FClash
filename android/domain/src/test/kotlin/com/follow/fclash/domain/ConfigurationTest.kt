package com.follow.fclash.domain

import com.google.gson.JsonObject
import java.io.*
import java.nio.file.Files
import java.util.zip.ZipEntry
import java.util.zip.ZipOutputStream
import kotlin.test.*

class ConfigurationTest {
    @Test fun scalarTypesAndOrderedRules() {
        val directory = Files.createTempDirectory("fclash-test").toFile()
        try {
            File(directory, "profiles").mkdirs()
            val profile = Profile(id = 1); val state = ClientState(); state.profiles.add(profile); state.currentId = 1
            File(directory, "profiles/1.yaml").writeText("proxies: []\nrules: ['DOMAIN,github.com,Proxy', 'MATCH,Proxy']")
            state.addedRules.addAll(listOf("DOMAIN-SUFFIX,dev.sijingkeji.cn,DIRECT", "GEOSITE,cn,DIRECT", "GEOIP,CN,MATCH,no-resolve"))
            state.config.addProperty("overrideDns", true)
            state.patch.add("dns", Configuration.parse("nameserver-policy:\n  '+.dev.sijingkeji.cn': system://\n  'geosite:cn': 223.5.5.5"))
            val yaml = Configuration.compile(state, profile, directory)
            assertTrue(yaml.contains("mixed-port: 7890\n")); assertFalse(yaml.contains("7890.0"))
            val result = Configuration.parse(yaml)
            assertEquals("DOMAIN-SUFFIX,dev.sijingkeji.cn,DIRECT", result.strings("rules")[0])
            assertEquals("GEOIP,CN,Proxy,no-resolve", result.strings("rules")[2])
            assertEquals(listOf("+.dev.sijingkeji.cn", "geosite:cn"), result.obj("dns").obj("nameserver-policy").keySet().toList())
            assertFalse(result.obj("tun").flag("enable"))
        } finally { directory.deleteRecursively() }
    }
    @Test fun customRulesAndDisabledRules() {
        val directory = Files.createTempDirectory("fclash-rules").toFile()
        try {
            File(directory, "profiles").mkdirs(); File(directory, "profiles/1.yaml").writeText("rules: ['MATCH,Proxy']")
            val profile = Profile(id = 1); val state = ClientState(); state.addedRules.add("GEOSITE,cn,DIRECT"); profile.disabledRules.add("GEOSITE,cn,DIRECT")
            assertEquals(listOf("MATCH,Proxy"), Configuration.parse(Configuration.compile(state, profile, directory)).strings("rules"))
            profile.overwriteType = "custom"; profile.customRules.add("MATCH,REJECT")
            assertEquals(listOf("MATCH,REJECT"), Configuration.parse(Configuration.compile(state, profile, directory)).strings("rules"))
        } finally { directory.deleteRecursively() }
    }
    @Test fun scriptsCannotAccessJavaAndAreBounded() {
        val config = JsonObject()
        assertTrue(Configuration.runScript("function main(c) { c.enabled = true; return c; }", config).flag("enabled"))
        assertFails { Configuration.runScript("function main(c) { while(true) {} }", config) }
        assertFails { Configuration.runScript("function main(c) { java.lang.System.exit(0); return c; }", config) }
    }
    @Test fun backupRoundTripAndTraversal() {
        val source = Files.createTempDirectory("fclash-source").toFile(); val target = Files.createTempDirectory("fclash-target").toFile()
        try {
            File(source, "native-state.json").writeText(json.toJson(ClientState()))
            File(source, "profiles").mkdirs(); File(source, "profiles/1.yaml").writeText("rules: [MATCH,DIRECT]")
            File(source, "GEOIP.dat").writeText("not in backup")
            val output = ByteArrayOutputStream(); Archive.backup(source, output); Archive.unpack(ByteArrayInputStream(output.toByteArray()), target)
            assertEquals("rules: [MATCH,DIRECT]", File(target, "profiles/1.yaml").readText()); assertFalse(File(target, "GEOIP.dat").exists())
            target.deleteRecursively(); target.mkdirs()
            val bad = ByteArrayOutputStream(); ZipOutputStream(bad).use { it.putNextEntry(ZipEntry("../escape")); it.write(1); it.closeEntry() }
            assertFails { Archive.unpack(ByteArrayInputStream(bad.toByteArray()), target) }
        } finally { source.deleteRecursively(); target.deleteRecursively() }
    }
    @Test fun stateAndCountryPersistenceArePerProfile() {
        val state = ClientState(); state.profiles.add(Profile(id = 1, label = "配置")); state.currentId = 1
        state.countries[state.countryKey(1, "节点")] = "JP"
        val restored = json.fromJson(json.toJson(state), ClientState::class.java)
        assertEquals("配置", restored.current!!.label); assertEquals("JP", restored.countries[restored.countryKey(1, "节点")]); assertNull(restored.countries[restored.countryKey(2, "节点")])
        assertEquals("[1,\"香港=<节点>\"]", restored.countryKey(1, "香港=<节点>"))
    }
    @Test fun duplicateYamlKeysFailClearly() { assertFails { Configuration.parse("a: 1\na: 2") } }
    @Test fun legacyArrayDnsPoliciesKeepOrderAndServers() {
        val legacy = Configuration.parse("policy: [{domain: '+.dev.sijingkeji.cn', server: 'system://'}, {domain: 'geosite:cn', server: ['223.5.5.5', '119.29.29.29']}]")
        val map = Configuration.policyMap(legacy.get("policy"))
        assertEquals(listOf("+.dev.sijingkeji.cn", "geosite:cn"), map.keySet().toList())
        assertEquals(listOf("system://"), map.getAsJsonArray("+.dev.sijingkeji.cn").map { it.asString })
        assertEquals(2, map.getAsJsonArray("geosite:cn").size())
    }
    @Test fun vpnOptionsRequireServiceRestartButDnsDoesNot() {
        val before = Configuration.parse("vpnProps: {enable: true}\npatchClashConfig: {mixed-port: 7890, dns: {enable: true}}")
        val dns = before.deepCopy().apply { obj("patchClashConfig").obj("dns").addProperty("ipv6", true) }
        assertFalse(Configuration.requiresVpnRestart(before, dns))
        val vpn = before.deepCopy().apply { obj("vpnProps").addProperty("systemProxy", false) }
        assertTrue(Configuration.requiresVpnRestart(before, vpn))
        val port = before.deepCopy().apply { obj("patchClashConfig").addProperty("mixed-port", 7891) }
        assertTrue(Configuration.requiresVpnRestart(before, port))
    }
}
