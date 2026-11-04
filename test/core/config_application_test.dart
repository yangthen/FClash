import 'package:fl_clash/config/application.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test(
    'failed digest can be retried and only success is deduplicated',
    () async {
      final app = ConfigApplication();
      addTearDown(app.state.dispose);
      await expectLater(
        app.apply(
          revision: app.request(),
          digest: 'one',
          operation: () async => throw StateError('invalid'),
        ),
        throwsStateError,
      );
      expect(app.state.value.phase, ConfigPhase.failed);
      var calls = 0;
      expect(
        await app.apply(
          revision: app.request(),
          digest: 'one',
          operation: () async {
            calls++;
          },
        ),
        true,
      );
      expect(
        await app.apply(
          revision: app.request(),
          digest: 'one',
          operation: () async {
            calls++;
          },
        ),
        false,
      );
      expect(calls, 1);
      expect(app.state.value.phase, ConfigPhase.applied);
    },
  );

  test('superseded configuration cannot be applied', () async {
    final app = ConfigApplication();
    addTearDown(app.state.dispose);
    final previous = app.request();
    app.request();
    var called = false;
    expect(
      await app.apply(
        revision: previous,
        digest: 'old',
        operation: () async {
          called = true;
        },
      ),
      false,
    );
    expect(called, false);
  });

  test('restarting core invalidates previously applied digest', () async {
    final app = ConfigApplication();
    addTearDown(app.state.dispose);
    await app.apply(
      revision: app.request(),
      digest: 'one',
      operation: () async {},
    );
    app.invalidate();
    expect(
      await app.apply(
        revision: app.request(),
        digest: 'one',
        operation: () async {},
      ),
      true,
    );
  });
}
