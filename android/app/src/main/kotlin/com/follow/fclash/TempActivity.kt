package com.follow.fclash

import android.app.Activity
import android.os.Bundle
import com.follow.clash.common.QuickAction
import com.follow.clash.common.action
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch

class TempActivity : Activity(),
    CoroutineScope by CoroutineScope(SupervisorJob() + Dispatchers.Default) {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        when (intent.action) {
            QuickAction.START.action -> {
                launch {
                    startOrRequestPermission()
                }
            }

            QuickAction.STOP.action -> {
                launch {
                    State.handleStopServiceAction()
                }
            }

            QuickAction.TOGGLE.action -> {
                launch {
                    if (Service.getRunTime() > 0) State.handleStopServiceAction() else startOrRequestPermission()
                }
            }
        }
        finish()
    }
    private suspend fun startOrRequestPermission() {
        if (BuildConfig.SAFE_MODE) return
        if (android.net.VpnService.prepare(applicationContext) != null) startActivity(android.content.Intent(this, MainActivity::class.java).putExtra("start", true))
        else State.handleStartServiceAction()
    }
}
