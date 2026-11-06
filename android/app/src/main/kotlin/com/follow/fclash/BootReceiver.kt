package com.follow.fclash

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import com.follow.clash.common.GlobalState
import com.follow.fclash.domain.flag
import com.follow.fclash.nativeapp.NativeRuntime
import kotlinx.coroutines.launch

class BootReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        if (intent.action != Intent.ACTION_BOOT_COMPLETED || BuildConfig.SAFE_MODE) return
        val pending = goAsync()
        GlobalState.launch {
            try { val session = NativeRuntime.get(context); session.load(); if (session.state.settings.flag("autoLaunch")) State.handleStartServiceAction() }
            finally { pending.finish() }
        }
    }
}
