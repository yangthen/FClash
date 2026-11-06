package com.follow.clash.service.modules

import android.Manifest
import android.app.Service
import android.content.pm.PackageManager
import android.net.wifi.WifiManager
import androidx.core.content.ContextCompat
import com.google.gson.JsonParser
import kotlinx.coroutines.*
import java.io.File

/** Keeps SSID decisions with the foreground VPN service, independent of any Activity. */
class OnDemandModule(private val service: Service, private val changed: (Boolean) -> Unit) : Module() {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    override fun onInstall() {
        scope.launch {
            var paused = false
            while (isActive) {
                val excluded = runCatching {
                    val file = File(service.filesDir, "native/native-state.json")
                    if (!file.exists()) emptyList() else JsonParser.parseString(file.readText()).asJsonObject.getAsJsonObject("Config")?.getAsJsonArray("excludeSSIDs")?.map { it.asString } ?: emptyList()
                }.getOrDefault(emptyList())
                @Suppress("DEPRECATION")
                val ssid = if (excluded.isNotEmpty() && ContextCompat.checkSelfPermission(service, Manifest.permission.ACCESS_FINE_LOCATION) == PackageManager.PERMISSION_GRANTED) (service.applicationContext.getSystemService(Service.WIFI_SERVICE) as? WifiManager)?.connectionInfo?.ssid?.trim('"') else null
                val next = ssid != null && ssid in excluded
                if (next != paused) { runCatching { changed(next) }.onSuccess { paused = next } }
                delay(5000)
            }
        }
    }
    override fun onUninstall() { scope.cancel() }
}
