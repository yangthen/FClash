import 'dart:async';
import 'dart:collection';

class EventBuffer<T> {
  final int capacity;
  final Duration interval;
  final void Function(List<T>) onFlush;
  final Queue<T> _pending = Queue();
  Timer? _timer;

  EventBuffer({
    required this.capacity,
    required this.onFlush,
    this.interval = const Duration(milliseconds: 250),
  });

  void add(T item) {
    if (capacity <= 0) return;
    _pending.addLast(item);
    while (_pending.length > capacity) {
      _pending.removeFirst();
    }
    _timer ??= Timer(interval, flush);
  }

  void flush() {
    _timer?.cancel();
    _timer = null;
    if (_pending.isEmpty) return;
    final batch = _pending.toList(growable: false);
    _pending.clear();
    onFlush(batch);
  }

  void dispose() {
    _timer?.cancel();
    _timer = null;
    _pending.clear();
  }
}
