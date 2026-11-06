import org.jetbrains.kotlin.gradle.dsl.JvmTarget
import java.util.Properties
import com.google.firebase.crashlytics.buildtools.gradle.CrashlyticsExtension

plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.compose")
    id("com.google.gms.google-services")
    id("com.google.firebase.crashlytics")
}

val localProperties = Properties().apply {
    val localPropertiesFile = rootProject.file("local.properties")
    if (localPropertiesFile.exists()) {
        localPropertiesFile.inputStream().use { load(it) }
    }
}

val signingProperties = Properties().apply {
    putAll(localProperties)
    val signingFile = rootProject.file("key.properties")
    if (signingFile.exists()) signingFile.inputStream().use { load(it) }
}
val mStoreFile: File = file(signingProperties.getProperty("storeFile", "keystore.jks"))
val mStorePassword: String? = signingProperties.getProperty("storePassword")
val mKeyAlias: String? = signingProperties.getProperty("keyAlias")
val mKeyPassword: String? = signingProperties.getProperty("keyPassword")
val isRelease =
    mStoreFile.exists() && mStorePassword != null && mKeyAlias != null && mKeyPassword != null


check(!providers.gradleProperty("fclash.requireReleaseSigning").isPresent || isRelease) { "FClash release signing is required" }
val appVersion = Properties().apply { rootProject.file("../version.properties").inputStream().use { load(it) } }

android {
    buildToolsVersion = "36.1.0"
    namespace = "com.follow.fclash"
    compileSdk = libs.versions.compileSdk.get().toInt()
    ndkVersion = libs.versions.ndkVersion.get()



    compileOptions {
        isCoreLibraryDesugaringEnabled = true
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    defaultConfig {
        applicationId = "com.follow.fclash"
        minSdk = libs.versions.minSdk.get().toInt()
        targetSdk = libs.versions.targetSdk.get().toInt()
        versionCode = appVersion.getProperty("code").toInt()
        versionName = appVersion.getProperty("label")
        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
        buildConfigField("boolean", "SAFE_MODE", providers.gradleProperty("fclash.safeMode").orElse("false").get())
        ndk { abiFilters += (providers.gradleProperty("fclash.abis").orNull?.split(',') ?: listOf("arm64-v8a", "armeabi-v7a", "x86_64")) }
    }

    buildFeatures { compose = true; buildConfig = true }
    sourceSets.getByName("main").assets.srcDirs("../../resources/geodata", "../../resources/translations")

    signingConfigs {
        if (isRelease) {
            create("release") {
                storeFile = mStoreFile
                storePassword = mStorePassword
                keyAlias = mKeyAlias
                keyPassword = mKeyPassword
            }
        }
    }

    packaging {
        jniLibs {
            useLegacyPackaging = true
        }
    }

    buildTypes {
        configureEach {
            // Local/CI builds must not publish maps or native symbols to the
            // legacy upstream Firebase project as a side effect of assembly.
            configure<CrashlyticsExtension> {
                mappingFileUploadEnabled = false
                nativeSymbolUploadEnabled = false
            }
        }
        debug {
            isMinifyEnabled = providers.gradleProperty("fclash.minifyDebug").orElse("false").get().toBoolean()
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
            applicationIdSuffix = ".dev"
        }

        release {
            isMinifyEnabled = true
            isShrinkResources = true
            if (isRelease) {
                signingConfig = signingConfigs.getByName("release")
            } else {
                signingConfig = signingConfigs.getByName("debug")
                applicationIdSuffix = ".dev"
            }

            proguardFiles(
                getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro"
            )
        }
    }
}

kotlin {
    compilerOptions {
        jvmTarget.set(JvmTarget.JVM_17)
    }
}




dependencies {
    androidTestImplementation(platform("androidx.compose:compose-bom:2025.10.01"))
    androidTestImplementation("androidx.compose.ui:ui-test-junit4")
    androidTestImplementation("androidx.test:runner:1.6.2")
    androidTestImplementation("androidx.test.ext:junit:1.2.1")
    debugImplementation("androidx.compose.ui:ui-test-manifest")
    coreLibraryDesugaring("com.android.tools:desugar_jdk_libs:2.1.5")
    implementation(project(":domain"))
    implementation(platform("androidx.compose:compose-bom:2025.10.01"))
    implementation("androidx.activity:activity-compose:1.12.4")
    implementation("androidx.fragment:fragment:1.8.9")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.compose.material:material-icons-extended")
    implementation("androidx.lifecycle:lifecycle-runtime-compose:2.9.4")
    implementation("androidx.lifecycle:lifecycle-viewmodel-compose:2.9.4")
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.10.2")
    implementation(project(":service"))
    implementation(project(":common"))
    implementation(libs.core.splashscreen)
    implementation(libs.gson)
    implementation(libs.smali.dexlib2) {
        exclude(group = "com.google.guava", module = "guava")
    }
    implementation(platform(libs.firebase.bom))
    implementation(libs.firebase.crashlytics.ndk)
    implementation(libs.firebase.analytics)
}
