import 'dart:io';

class ConfigStore {
  Future<bool> apply({
    required String path,
    required String content,
    required Future<String> Function(String path) validate,
    required Future<String> Function() activate,
    required bool Function() isCurrent,
  }) async {
    final target = File(path);
    await target.parent.create(recursive: true);
    final candidate = File(
      '$path.${DateTime.now().microsecondsSinceEpoch}.pending',
    );
    try {
      await candidate.writeAsString(content, flush: true);
      final validation = await validate(candidate.path);
      if (validation.isNotEmpty) throw StateError(validation);
      if (!isCurrent()) return false;
      final previous = await target.exists()
          ? await target.readAsBytes()
          : null;
      await candidate.rename(target.path);
      try {
        final error = await activate();
        if (error.isNotEmpty) throw StateError(error);
      } catch (_) {
        if (previous == null) {
          await target.delete();
        } else {
          await candidate.writeAsBytes(previous, flush: true);
          await candidate.rename(target.path);
        }
        rethrow;
      }
      return true;
    } finally {
      if (await candidate.exists()) await candidate.delete();
    }
  }
}
