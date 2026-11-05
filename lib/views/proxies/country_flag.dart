import 'package:fl_clash/providers/providers.dart';
import 'package:fl_clash/widgets/country_flag.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

class ProxyCountryFlag extends ConsumerWidget {
  final String proxyName;

  const ProxyCountryFlag({super.key, required this.proxyName});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final profileId = ref.watch(currentProfileIdProvider);
    final key = proxyCountryKey(profileId, proxyName);
    final country = ref.watch(
      proxyCountriesProvider.select((value) => value.asData?.value[key]),
    );
    return country == null
        ? const SizedBox.shrink()
        : Padding(
            padding: const EdgeInsets.only(left: 8),
            child: CountryFlag(countryCode: country),
          );
  }
}
