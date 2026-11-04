import 'dart:convert';
import 'package:fl_clash/models/clash_config.dart';
import 'package:fl_clash/providers/config.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('legacy policy map preserves order through serialization', () {
    final dns = Dns.fromJson({
      'nameserver-policy': {
        '+.dev.example.cn': 'system',
        'geosite:cn': 'https://doh.pub/dns-query',
      },
    });
    final restored = Dns.fromJson(
      jsonDecode(jsonEncode(dns)) as Map<String, dynamic>,
    );
    expect(restored, dns);
    expect(restored.nameserverPolicy.map((item) => item.domain), [
      '+.dev.example.cn',
      'geosite:cn',
    ]);
  });

  test('reordering alone updates DNS provider state', () {
    final container = ProviderContainer();
    addTearDown(container.dispose);
    final subscription = container.listen(patchClashConfigProvider, (_, _) {});
    addTearDown(subscription.close);
    final notifier = container.read(patchClashConfigProvider.notifier);
    final before = container
        .read(patchClashConfigProvider)
        .dns
        .nameserverPolicy;
    notifier.update(
      (value) => value.copyWith.dns(nameserverPolicy: before.reversed.toList()),
    );
    expect(
      container.read(patchClashConfigProvider).dns.nameserverPolicy,
      before.reversed.toList(),
    );
  });

  test('new rules have distinct identities', () {
    expect(Rule.init().id, isNot(Rule.init().id));
  });
}
