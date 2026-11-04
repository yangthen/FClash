import 'dart:async';
import 'dart:ui';

extension FutureExt<T> on Future<T> {
  Future<T> withTimeout({
    Duration? timeout,
    String? tag,
    VoidCallback? onLast,
    FutureOr<T> Function()? onTimeout,
  }) {
    final realTimeout = timeout ?? const Duration(minutes: 3);
    return this
        .timeout(
          realTimeout,
          onTimeout: () async {
            if (onTimeout != null) {
              return onTimeout();
            } else {
              throw TimeoutException('${tag ?? runtimeType} timeout');
            }
          },
        )
        .whenComplete(() => onLast?.call());
  }
}

extension CompleterExt<T> on Completer<T> {
  void safeCompleter(T value) {
    if (isCompleted) {
      return;
    }
    complete(value);
  }
}
