package com.follow.fclash

import com.follow.clash.common.GlobalState
import com.follow.fclash.nativeapp.NativeRuntime
import kotlinx.coroutines.flow.MutableStateFlow

enum class RunState { START, PENDING, STOP }

/** Quick settings and service broadcasts share the same native lifecycle. */
object State {
    val runStateFlow = MutableStateFlow(RunState.STOP)
    suspend fun handleSyncState() { if (BuildConfig.SAFE_MODE) { runStateFlow.value = RunState.STOP; return }; runStateFlow.value = if (Service.getRunTime() > 0) RunState.START else RunState.STOP }
    suspend fun handleToggleAction() { handleSyncState(); if (runStateFlow.value == RunState.START) handleStopServiceAction() else handleStartServiceAction() }
    suspend fun handleStartServiceAction() {
        val session = NativeRuntime.get(GlobalState.application)
        runStateFlow.value = RunState.PENDING
        try { session.load(); session.start(); runStateFlow.value = RunState.START }
        catch (error: Exception) { session.report(error); runStateFlow.value = RunState.STOP }
    }
    suspend fun handleStopServiceAction() {
        val session = NativeRuntime.get(GlobalState.application)
        runStateFlow.value = RunState.PENDING
        try { session.stop() } finally { runStateFlow.value = RunState.STOP }
    }
}
