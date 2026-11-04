import 'package:drift/native.dart';
import 'package:fl_clash/database/database.dart';
import 'package:fl_clash/models/models.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('global rule editor and compiled profile read the same order', () async {
    final db = Database(NativeDatabase.memory());
    addTearDown(db.close);
    await db.rulesDao.putGlobalRule(
      const Rule(
        id: 1,
        content: 'first.example',
        ruleTarget: 'DIRECT',
        order: 'a0',
      ),
    );
    await db.rulesDao.putGlobalRule(
      const Rule(
        id: 2,
        content: 'second.example',
        ruleTarget: 'DIRECT',
        order: 'a1',
      ),
    );
    expect(
      (await db.rulesDao.queryAddedRules(10).get()).map((rule) => rule.id),
      (await db.rulesDao.queryGlobalAddedRules().get()).map((rule) => rule.id),
    );
    await db.rulesDao.orderGlobalRule(ruleId: 2, order: 'Zz');
    final visible = await db.rulesDao.queryGlobalAddedRules().get();
    final applied = await db.rulesDao.queryAddedRules(10).get();
    expect(visible.map((rule) => rule.id), [2, 1]);
    expect(applied.map((rule) => rule.id), visible.map((rule) => rule.id));
  });
}
