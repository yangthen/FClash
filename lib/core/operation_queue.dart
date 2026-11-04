import 'dart:async';

class OperationQueue {
  final Object _zoneKey = Object();
  Future<void> _pending = Future.value();
  Object? _activeLease;

  Future<T> run<T>(Future<T> Function() operation) {
    if (_activeLease != null &&
        identical(Zone.current[_zoneKey], _activeLease)) {
      return operation();
    }
    final result = _pending.then((_) async {
      final lease = Object();
      _activeLease = lease;
      try {
        return await runZoned(operation, zoneValues: {_zoneKey: lease});
      } finally {
        _activeLease = null;
      }
    });
    _pending = result.then<void>((_) {}, onError: (Object _, StackTrace _) {});
    return result;
  }

  Future<void> get settled => _pending;
}
