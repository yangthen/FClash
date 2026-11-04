import 'dart:async';
import 'package:fl_clash/core/operation_queue.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test(
    'deferred callback from a completed operation cannot bypass the queue',
    () async {
      final queue = OperationQueue();
      final order = <int>[];
      late Future<void> Function() deferred;
      await queue.run(() async {
        deferred = Zone.current.bindCallback(
          () => queue.run(() async {
            order.add(3);
          }),
        );
      });
      final started = Completer<void>();
      final gate = Completer<void>();
      final active = queue.run(() async {
        order.add(1);
        started.complete();
        await gate.future;
        order.add(2);
      });
      await started.future;
      final delayed = deferred();
      expect(order, [1]);
      gate.complete();
      await Future.wait([active, delayed]);
      expect(order, [1, 2, 3]);
    },
  );
  test(
    'serializes async transitions and supports nested transitions',
    () async {
      final queue = OperationQueue();
      final gate = Completer<void>();
      final order = <int>[];
      final first = queue.run(() async {
        order.add(1);
        await gate.future;
        await queue.run(() async {
          order.add(2);
        });
      });
      final second = queue.run(() async {
        order.add(3);
      });
      await Future<void>.delayed(Duration.zero);
      expect(order, [1]);
      gate.complete();
      await Future.wait([first, second]);
      expect(order, [1, 2, 3]);
    },
  );

  test('failed operation does not poison subsequent operations', () async {
    final queue = OperationQueue();
    await expectLater(
      queue.run(() async => throw StateError('failed')),
      throwsStateError,
    );
    expect(await queue.run(() async => 42), 42);
  });
}
