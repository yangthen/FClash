import 'dart:convert';
import 'package:fl_clash/config/compiler.dart';
import 'package:fl_clash/models/models.dart';
import 'package:flutter_test/flutter_test.dart';

MakeRealProfileState input(PatchClashConfig patch, List<Rule> rules) =>
    MakeRealProfileState(
      profilesPath: 'profiles',
      profileId: 1,
      rawConfig: {
        'rules': ['MATCH,DIRECT'],
      },
      realPatchConfig: patch,
      overrideDns: true,
      appendSystemDns: false,
      proxyGroups: [],
      rules: [],
      addedRules: rules,
      defaultUA: 'test',
    );

void main() {
  test('DNS editor order survives persistence and YAML compilation', () async {
    const dns = Dns(
      nameserverPolicy: [
        DnsPolicy(domain: '+.dev.example.cn', server: 'system'),
        DnsPolicy(domain: 'geosite:cn', server: 'https://doh.pub/dns-query'),
      ],
      defaultNameserver: ['223.5.5.5', '119.29.29.29'],
      nameserver: [
        'https://first.example/dns-query',
        'https://second.example/dns-query',
      ],
      fallback: ['tls://8.8.4.4', 'tls://1.1.1.1'],
      proxyServerNameserver: [
        'https://proxy-first.example/dns-query',
        'https://proxy-second.example/dns-query',
      ],
    );
    final restored = PatchClashConfig.fromJson(
      jsonDecode(jsonEncode(const PatchClashConfig(dns: dns)))
          as Map<String, dynamic>,
    );
    final yaml = (await compileProfile(input(restored, []))).a;
    for (final pair in [
      ['+.dev.example.cn', 'geosite:cn'],
      ['223.5.5.5', '119.29.29.29'],
      ['https://first.example/dns-query', 'https://second.example/dns-query'],
      ['tls://8.8.4.4', 'tls://1.1.1.1'],
      [
        'https://proxy-first.example/dns-query',
        'https://proxy-second.example/dns-query',
      ],
    ]) {
      expect(yaml.indexOf(pair.first), greaterThanOrEqualTo(0));
      expect(yaml.indexOf(pair.first), lessThan(yaml.indexOf(pair.last)));
    }
    final reordered = restored.copyWith.dns(
      nameserverPolicy: restored.dns.nameserverPolicy.reversed.toList(),
    );
    final nextYaml = (await compileProfile(input(reordered, []))).a;
    expect(
      nextYaml.indexOf('geosite:cn'),
      lessThan(nextYaml.indexOf('+.dev.example.cn')),
    );
  });

  test('added rules keep visible order ahead of subscription rules', () async {
    final yaml = (await compileProfile(
      input(const PatchClashConfig(), [
        const Rule(id: 2, content: 'second.example', ruleTarget: 'DIRECT'),
        const Rule(id: 1, content: 'first.example', ruleTarget: 'DIRECT'),
      ]),
    )).a;
    expect(
      yaml.indexOf('second.example'),
      lessThan(yaml.indexOf('first.example')),
    );
    expect(
      yaml.indexOf('first.example'),
      lessThan(yaml.indexOf('MATCH,DIRECT')),
    );
  });
}
