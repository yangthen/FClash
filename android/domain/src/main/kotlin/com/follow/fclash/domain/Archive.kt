package com.follow.fclash.domain

import java.io.File
import java.io.InputStream
import java.io.OutputStream
import java.util.zip.ZipEntry
import java.util.zip.ZipInputStream
import java.util.zip.ZipOutputStream

object Archive {
    fun backup(directory: File, output: OutputStream) = ZipOutputStream(output).use { zip ->
        directory.walkTopDown().filter { it.isFile && (it.name == "native-state.json" || it.relativeTo(directory).path.replace('\\', '/').startsWith("profiles/") || it.relativeTo(directory).path.replace('\\', '/').startsWith("scripts/")) }.forEach { file ->
            zip.putNextEntry(ZipEntry(file.relativeTo(directory).path.replace('\\', '/'))); file.inputStream().use { it.copyTo(zip) }; zip.closeEntry()
        }
    }
    fun unpack(input: InputStream, target: File) {
        require(!target.exists() || target.listFiles().isNullOrEmpty()) { "恢复暂存目录必须为空" }
        target.mkdirs()
        val root = target.canonicalPath + File.separator
        var total = 0L; var count = 0
        ZipInputStream(input).use { zip ->
            while (true) {
                val entry = zip.nextEntry ?: break
                require(++count < 10000) { "备份条目过多" }
                val file = File(target, entry.name)
                require(!entry.name.contains('\\') && file.canonicalPath.startsWith(root)) { "备份包含非法路径" }
                if (entry.isDirectory) file.mkdirs() else {
                    file.parentFile?.mkdirs()
                    file.outputStream().use { output ->
                        val buffer = ByteArray(8192)
                        while (true) { val length = zip.read(buffer); if (length < 0) break; total += length; require(total <= 128L * 1024 * 1024) { "备份过大" }; output.write(buffer, 0, length) }
                    }
                }
                zip.closeEntry()
            }
        }
    }
}
