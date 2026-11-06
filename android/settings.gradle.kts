pluginManagement {
    buildscript {
        repositories { google(); mavenCentral() }
        dependencies { classpath("com.android.tools:r8:9.1.57") }
    }
    repositories { google(); mavenCentral(); gradlePluginPortal() }
}
plugins {
    id("com.android.application") version "9.0.1" apply false
    id("com.android.library") version "9.0.1" apply false
    id("org.jetbrains.kotlin.android") version "2.4.0" apply false
    id("org.jetbrains.kotlin.jvm") version "2.4.0" apply false
    id("org.jetbrains.kotlin.plugin.compose") version "2.4.0" apply false
    id("com.google.gms.google-services") version "4.5.0" apply false
    id("com.google.firebase.crashlytics") version "3.0.7" apply false
}
rootProject.name = "FClash"
include(":app", ":core", ":service", ":common", ":domain")
