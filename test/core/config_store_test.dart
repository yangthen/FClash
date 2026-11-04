import 'dart:io';
import 'package:fl_clash/config/store.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  late Directory directory;
  late File file;
  setUp(() async {
    directory = await Directory.systemTemp.createTemp('FClash-config-test-');
    file = File('${directory.path}/config.yaml');
    await file.writeAsString('previous');
  });
  tearDown(() => directory.delete(recursive: true));

  test('validation failure preserves saved configuration', () async {
    await expectLater(
      ConfigStore().apply(
        path: file.path,
        content: 'next',
        validate: (_) async => 'invalid',
        activate: () async => '',
        isCurrent: () => true,
      ),
      throwsStateError,
    );
    expect(await file.readAsString(), 'previous');
    expect(await directory.list().length, 1);
  });
  test('activation failure restores previous file', () async {
    await expectLater(
      ConfigStore().apply(
        path: file.path,
        content: 'next',
        validate: (_) async => '',
        activate: () async => 'failed',
        isCurrent: () => true,
      ),
      throwsStateError,
    );
    expect(await file.readAsString(), 'previous');
    expect(await directory.list().length, 1);
  });
  test('success atomically replaces saved configuration', () async {
    await ConfigStore().apply(
      path: file.path,
      content: 'next',
      validate: (_) async => '',
      activate: () async => '',
      isCurrent: () => true,
    );
    expect(await file.readAsString(), 'next');
    expect(await directory.list().length, 1);
  });
}
