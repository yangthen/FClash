plugins { id("org.jetbrains.kotlin.jvm") }
kotlin { jvmToolchain(21) }
dependencies {
 implementation("com.google.code.gson:gson:2.13.1")
 implementation("org.yaml:snakeyaml:2.4")
 implementation("org.mozilla:rhino:1.8.0")
 testImplementation(kotlin("test"))
}
