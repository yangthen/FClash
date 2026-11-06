package com.follow.fclash

import android.app.Application
import android.os.Handler
import android.os.Looper
import android.widget.Toast

private var lastToast: Toast? = null
fun Application.showToast(text: String?) {
    Handler(Looper.getMainLooper()).post { lastToast?.cancel(); lastToast = Toast.makeText(this, text, Toast.LENGTH_LONG).apply { show() } }
}
