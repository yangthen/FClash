import 'dart:async';

class PendingRequests {
  final Map<String, Completer<dynamic>> _pending = {};

  int get length => _pending.length;

  Future<T> run<T>(
    String id,
    FutureOr<void> Function() send, {
    required Duration timeout,
  }) async {
    final completer = Completer<T>();
    _pending[id] = completer;
    try {
      unawaited(
        Future<void>.sync(send).then<void>(
          (_) {},
          onError: (Object error, StackTrace stack) {
            fail(id, error, stack);
          },
        ),
      );
      return await completer.future.timeout(timeout);
    } finally {
      _pending.remove(id);
    }
  }

  void complete(String? id, dynamic data) {
    final completer = _pending.remove(id);
    if (completer != null && !completer.isCompleted) {
      completer.complete(data);
    }
  }

  void fail(String? id, Object error, [StackTrace? stackTrace]) {
    final completer = _pending.remove(id);
    if (completer != null && !completer.isCompleted) {
      completer.completeError(error, stackTrace);
    }
  }

  void disconnect() {
    final pending = _pending.values.toList();
    _pending.clear();
    for (final completer in pending) {
      if (!completer.isCompleted) {
        completer.completeError(StateError('Core disconnected'));
      }
    }
  }
}
