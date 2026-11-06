package com.follow.fclash

import android.Manifest
import android.content.Intent
import android.net.VpnService
import android.os.Build
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.enableEdgeToEdge
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.lifecycle.lifecycleScope
import androidx.lifecycle.repeatOnLifecycle
import androidx.lifecycle.Lifecycle
import com.follow.fclash.domain.obj
import com.follow.fclash.domain.flag
import com.follow.fclash.nativeapp.NativeRuntime
import com.follow.fclash.nativeapp.FClashApp
import kotlinx.coroutines.launch

class MainActivity : ComponentActivity() {
    private val session by lazy { NativeRuntime.get(applicationContext) }
    private var queuedStart = false
    private val vpnPermission = registerForActivityResult(ActivityResultContracts.StartActivityForResult()) { result ->
        if (result.resultCode == RESULT_OK) startProxy()
    }
    private val notificationPermission = registerForActivityResult(ActivityResultContracts.RequestPermission()) { requestVpn() }
    private val locationPermission = registerForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) { }
    private val incoming = androidx.compose.runtime.mutableStateOf<String?>(null)
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        incoming.value = intent.dataString
        setContent { FClashApp(session, ::requestStart, { locationPermission.launch(arrayOf(Manifest.permission.ACCESS_FINE_LOCATION, Manifest.permission.ACCESS_COARSE_LOCATION)) }, incoming.value) { incoming.value = null } }
        lifecycleScope.launch {
            repeatOnLifecycle(Lifecycle.State.STARTED) {
                try { session.attach() } catch (e: Exception) { session.report(e) }
                try { kotlinx.coroutines.awaitCancellation() } finally { kotlinx.coroutines.withContext(kotlinx.coroutines.NonCancellable) { session.detach() } }
            }
        }
        queuedStart = intent.getBooleanExtra("start", false)
        lifecycleScope.launch { session.load(); if ((queuedStart || session.state.settings.flag("autoRun")) && !session.safe) { queuedStart = false; requestStart() } }
    }
    override fun onNewIntent(intent: Intent) { super.onNewIntent(intent); setIntent(intent); incoming.value = intent.dataString }
    private fun requestStart() {
        if (session.safe) return
        if (Build.VERSION.SDK_INT >= 33 && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != android.content.pm.PackageManager.PERMISSION_GRANTED) notificationPermission.launch(Manifest.permission.POST_NOTIFICATIONS) else requestVpn()
    }
    private fun requestVpn() {
        val enable = session.state.config.obj("vpnProps").flag("enable", true)
        val request = if (enable) VpnService.prepare(this) else null
        if (request != null) vpnPermission.launch(request) else startProxy()
    }
    private fun startProxy() { lifecycleScope.launch { try { session.start(); State.handleSyncState() } catch (error: Exception) { session.report(error) } } }
}
