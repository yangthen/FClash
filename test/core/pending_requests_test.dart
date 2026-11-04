import 'dart:async';
import 'package:fl_clash/core/pending_requests.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('responses immediately release pending requests', () async {
    final pending = PendingRequests();
    final future = pending.run<String>(
      'one',
      () => pending.complete('one', 'ok'),
      timeout: const Duration(seconds: 1),
    );
    expect(await future, 'ok');
    expect(pending.length, 0);
    pending.complete('one', 'late');
    expect(pending.length, 0);
  });

  test('timeout releases request and rejects late results', () async {
    final pending = PendingRequests();
    await expectLater(
      pending.run<String>('one', () {}, timeout: Duration.zero),
      throwsA(isA<TimeoutException>()),
    );
    expect(pending.length, 0);
    pending.complete('one', 'late');
  });

  test('disconnect fails every in-flight request', () async {
    final pending = PendingRequests();
    final future = pending.run<String>(
      'one',
      pending.disconnect,
      timeout: const Duration(seconds: 1),
    );
    await expectLater(future, throwsStateError);
    expect(pending.length, 0);
  });

  test('send failures release request', () async {
    final pending = PendingRequests();
    await expectLater(
      pending.run<String>(
        'one',
        () => throw StateError('send failed'),
        timeout: const Duration(seconds: 1),
      ),
      throwsStateError,
    );
    expect(pending.length, 0);
  });
}
