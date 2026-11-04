import 'package:fl_clash/providers/database.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('withRollback', () {
    test('rolls back with snapshot and rethrows async errors', () async {
      final error = StateError('write failed');
      final previous = [1, 2, 3];
      List<int>? rolledBack;

      await expectLater(
        withRollback(
          snapshot: previous,
          action: () async {
            throw error;
          },
          rollback: (value) => rolledBack = value,
        ),
        throwsA(same(error)),
      );

      expect(rolledBack, previous);
    });

    test('failed older mutation cannot overwrite newer state', () async {
      var state = 'new edit';
      await expectLater(
        withRollback(
          snapshot: 'old edit',
          action: () async => throw StateError('old write failed'),
          rollback: (value) => state = value,
          canRollback: () => false,
        ),
        throwsStateError,
      );
      expect(state, 'new edit');
    });

    test('does not roll back when action succeeds', () async {
      var rollbackCalled = false;

      await withRollback(
        snapshot: [1, 2, 3],
        action: () async {},
        rollback: (_) => rollbackCalled = true,
      );

      expect(rollbackCalled, false);
    });
  });
}
