import 'dart:async';
import 'dart:convert';

import 'package:fl_clash/common/preferences.dart';
import 'package:fl_clash/providers/proxy_country.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:riverpod/riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  late ProviderContainer container;
  late SharedPreferences storage;
  late Completer<SharedPreferences?> previous;

  setUp(() async {
    SharedPreferences.setMockInitialValues({});
    storage = await SharedPreferences.getInstance();
    previous = preferences.sharedPreferencesCompleter;
    await previous.future;
    preferences.sharedPreferencesCompleter = Completer()..complete(storage);
  });

  tearDown(() {
    container.dispose();
    preferences.sharedPreferencesCompleter = previous;
  });

  ProviderContainer create(Future<String?> Function(String) lookup) =>
      ProviderContainer(
        overrides: [proxyCountryLookupProvider.overrideWithValue(lookup)],
      );

  test(
    'load does not probe, and a successful result survives restart',
    () async {
      var calls = 0;
      container = create((_) async {
        calls++;
        return 'jp';
      });
      expect(await container.read(proxyCountriesProvider.future), isEmpty);
      expect(calls, 0);
      await container.read(proxyCountriesProvider.notifier).refresh(1, 'node');
      expect(calls, 1);
      container.dispose();
      container = create((_) async => throw StateError('must not probe'));
      expect(await container.read(proxyCountriesProvider.future), {
        proxyCountryKey(1, 'node'): 'JP',
      });
    },
  );

  test('failure and invalid responses retain last successful flag', () async {
    String? result = 'US';
    container = create((_) async => result);
    final notifier = container.read(proxyCountriesProvider.notifier);
    await notifier.refresh(1, 'node');
    for (final failure in [null, '', 'UNKNOWN', '12']) {
      result = failure;
      await notifier.refresh(1, 'node');
      expect(container.read(proxyCountriesProvider).requireValue, {
        proxyCountryKey(1, 'node'): 'US',
      });
    }
    result = 'DE';
    await notifier.refresh(1, 'node');
    expect(container.read(proxyCountriesProvider).requireValue.values, ['DE']);
  });

  test('latest test wins when requests finish in reverse order', () async {
    final requests = <Completer<String?>>[];
    container = create((_) {
      final request = Completer<String?>();
      requests.add(request);
      return request.future;
    });
    await container.read(proxyCountriesProvider.future);
    final notifier = container.read(proxyCountriesProvider.notifier);
    final older = notifier.refresh(1, 'node');
    final newer = notifier.refresh(1, 'node');
    await Future<void>.delayed(Duration.zero);
    requests[1].complete('JP');
    await newer;
    requests[0].complete('US');
    await older;
    expect(container.read(proxyCountriesProvider).requireValue.values, ['JP']);
  });

  test(
    'same node names in different profiles have separate cached flags',
    () async {
      var result = 'US';
      container = create((_) async => result);
      final notifier = container.read(proxyCountriesProvider.notifier);
      await notifier.refresh(1, 'node');
      result = 'JP';
      await notifier.refresh(2, 'node');
      final saved = jsonDecode(storage.getString('proxyCountries')!) as Map;
      expect(saved, {
        proxyCountryKey(1, 'node'): 'US',
        proxyCountryKey(2, 'node'): 'JP',
      });
    },
  );

  test('corrupt cache does not prevent new lookups', () async {
    await storage.setString('proxyCountries', 'invalid json');
    container = create((_) async => 'HK');
    expect(await container.read(proxyCountriesProvider.future), isEmpty);
    await container.read(proxyCountriesProvider.notifier).refresh(1, 'node');
    expect(container.read(proxyCountriesProvider).requireValue.values, ['HK']);
  });

  test('group card keeps its result until another explicit test', () async {
    var selected = 'node-a';
    container = create((name) async => name == 'node-a' ? 'US' : 'JP');
    final notifier = container.read(proxyCountriesProvider.notifier);
    await notifier.refresh(1, 'group', lookupName: selected);
    selected = 'node-b';
    expect(container.read(proxyCountriesProvider).requireValue, {
      proxyCountryKey(1, 'group'): 'US',
    });
    await notifier.refresh(1, 'group', lookupName: selected);
    expect(container.read(proxyCountriesProvider).requireValue, {
      proxyCountryKey(1, 'group'): 'JP',
    });
  });
}
