import 'package:fl_clash/common/event_buffer.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('batches events and bounds retained backlog', () {
    final batches = <List<int>>[];
    final buffer = EventBuffer<int>(capacity: 3, onFlush: batches.add);
    addTearDown(buffer.dispose);
    for (var i = 0; i < 10; i++) {
      buffer.add(i);
    }
    expect(batches, isEmpty);
    buffer.flush();
    expect(batches, [
      [7, 8, 9],
    ]);
    buffer.flush();
    expect(batches.length, 1);
  });

  test('disposing drops queued events', () {
    final batches = <List<int>>[];
    final buffer = EventBuffer<int>(capacity: 3, onFlush: batches.add);
    buffer.add(1);
    buffer.dispose();
    buffer.flush();
    expect(batches, isEmpty);
  });
}
