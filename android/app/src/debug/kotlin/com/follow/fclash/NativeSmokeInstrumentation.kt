package com.follow.fclash

import android.app.Activity
import android.app.Instrumentation
import android.os.Bundle
import com.follow.fclash.domain.*
import com.follow.fclash.nativeapp.NativeStore
import java.io.File

/** In-APK runner avoids a second, differently shrunk desugaring library.
 * Only packaged in debug builds and restricted to compile-time safe mode. */
class NativeSmokeInstrumentation : Instrumentation() {
    override fun onCreate(arguments: Bundle?) { super.onCreate(arguments); start() }
    override fun onStart() {
        val result = Bundle()
        val outcome = runCatching {
            check(BuildConfig.SAFE_MODE) { "Use a dedicated safe-mode app" }
            val input = Configuration.parse("mixed-port: 7890\nipv6: false\nrules: ['MATCH,DIRECT']")
            val output = Configuration.runScript("function main(c) { c['mixed-port'] = 7891; c.rules.unshift('GEOSITE,cn,DIRECT'); return c; }", input)
            val roundTrip = Configuration.parse(Configuration.yaml(output))
            check(roundTrip.get("mixed-port").asInt == 7891 && !roundTrip.get("ipv6").asBoolean)
            check(roundTrip.strings("rules")[0] == "GEOSITE,cn,DIRECT")
            check(runCatching { Configuration.runScript("function main(c) { while(true) {} }", input) }.isFailure)
            check(runCatching { Configuration.runScript("function main(c) { c.x=java.lang.System.getProperty('user.home'); return c; }", input) }.isFailure)
            val root = File(targetContext.cacheDir, "native-r8-${System.nanoTime()}")
            try {
                val store = NativeStore(targetContext, true, root)
                val state = ClientState().apply { profiles.add(Profile(id = 1)); currentId = 1 }
                store.atomic(store.profile(1), "rules: ['MATCH,DIRECT']")
                state.addedRules.addAll(listOf("GEOSITE,cn,DIRECT", "DOMAIN-SUFFIX,example.invalid,DIRECT"))
                store.save(state)
                val loaded = store.load()
                check(Configuration.parse(Configuration.compile(loaded, loaded.current!!, root)).strings("rules").take(2) == loaded.addedRules)
            } finally { root.deleteRecursively() }
            result.putString("stream", "PASS: minified Android scripts, execution bounds, Java isolation, YAML types/order and persisted models\n")
        }
        outcome.exceptionOrNull()?.let { result.putString("stream", it.stackTraceToString()) }
        result.putBoolean("passed", outcome.isSuccess)
        finish(if (outcome.isSuccess) Activity.RESULT_OK else Activity.RESULT_CANCELED, result)
    }
}
