import 'package:drift/native.dart';
import 'package:fl_clash/database/database.dart';
import 'package:fl_clash/models/models.dart';
import 'package:fl_clash/providers/database.dart' as providers;
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

class _Rules extends providers.GlobalRules {
  final Database db;
  _Rules(this.db);

  @override
  Database get store => db;
}

void main() {
  test(
    'rapid consecutive drags keep valid keys and persist visible order',
    () async {
      final db = Database(NativeDatabase.memory());
      addTearDown(db.close);
      for (var i = 0; i < 3; i++) {
        await db.rulesDao.putGlobalRule(
          Rule(
            id: i + 1,
            content: '$i.example',
            ruleTarget: 'DIRECT',
            order: 'a$i',
          ),
        );
      }
      final container = ProviderContainer(
        overrides: [
          providers.globalRulesProvider.overrideWith(() => _Rules(db)),
        ],
      );
      addTearDown(container.dispose);
      final subscription = container.listen(
        providers.globalRulesProvider,
        (_, _) {},
      );
      addTearDown(subscription.close);
      await container.read(providers.globalRulesProvider.future);
      final notifier = container.read(providers.globalRulesProvider.notifier);
      notifier.order(2, 0);
      notifier.order(2, 1);
      expect(
        container
            .read(providers.globalRulesProvider)
            .value!
            .map((rule) => rule.id),
        [3, 2, 1],
      );
      await providers.flushDatabaseWrites();
      expect(
        (await db.rulesDao.queryGlobalAddedRules().get()).map(
          (rule) => rule.id,
        ),
        [3, 2, 1],
      );
      expect(
        (await db.rulesDao.queryAddedRules(10).get()).map((rule) => rule.id),
        [3, 2, 1],
      );
    },
  );
}
