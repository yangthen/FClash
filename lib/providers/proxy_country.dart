import 'dart:convert';

import 'package:fl_clash/common/common.dart';
import 'package:fl_clash/core/controller.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'generated/proxy_country.g.dart';

String proxyCountryKey(int? profileId, String name) =>
    jsonEncode([profileId, name]);

@riverpod
Future<String?> Function(String) proxyCountryLookup(Ref ref) =>
    coreController.getProxyCountry;

@Riverpod(keepAlive: true)
class ProxyCountries extends _$ProxyCountries {
  final Map<String, int> _versions = {};
  Future<void> _save = Future.value();

  @override
  Future<Map<String, String>> build() => preferences.getProxyCountries();

  // Reading/rendering cards never starts a lookup. Only explicit delay tests
  // call refresh; failures leave the last successful country in place.
  Future<void> refresh(
    int? profileId,
    String name, {
    String? lookupName,
  }) async {
    final key = proxyCountryKey(profileId, name);
    final version = (_versions[key] ?? 0) + 1;
    _versions[key] = version;
    await future;
    if (!ref.mounted) return;
    try {
      final code = await ref.read(proxyCountryLookupProvider)(
        lookupName ?? name,
      );
      if (!ref.mounted || _versions[key] != version || code == null) return;
      final normalized = code.toUpperCase();
      if (!RegExp(r'^[A-Z]{2}$').hasMatch(normalized)) return;
      final countries = {...state.requireValue, key: normalized};
      state = AsyncData(countries);
      _save = _save
          .then((_) => preferences.saveProxyCountries(countries))
          .catchError((Object error) {
            commonPrint.log('Proxy country cache save failed: $error');
          });
      await _save;
    } catch (error) {
      commonPrint.log('Proxy country lookup failed: $error');
    }
  }
}
