// GENERATED CODE - DO NOT MODIFY BY HAND

part of '../proxy_country.dart';

// **************************************************************************
// RiverpodGenerator
// **************************************************************************

// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, type=warning

@ProviderFor(proxyCountryLookup)
final proxyCountryLookupProvider = ProxyCountryLookupProvider._();

final class ProxyCountryLookupProvider
    extends
        $FunctionalProvider<
          Future<String?> Function(String),
          Future<String?> Function(String),
          Future<String?> Function(String)
        >
    with $Provider<Future<String?> Function(String)> {
  ProxyCountryLookupProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'proxyCountryLookupProvider',
        isAutoDispose: true,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$proxyCountryLookupHash();

  @$internal
  @override
  $ProviderElement<Future<String?> Function(String)> $createElement(
    $ProviderPointer pointer,
  ) => $ProviderElement(pointer);

  @override
  Future<String?> Function(String) create(Ref ref) {
    return proxyCountryLookup(ref);
  }

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(Future<String?> Function(String) value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<Future<String?> Function(String)>(
        value,
      ),
    );
  }
}

String _$proxyCountryLookupHash() =>
    r'c93de8f0668bebde63d164c1404010756b9fc7b6';

@ProviderFor(ProxyCountries)
final proxyCountriesProvider = ProxyCountriesProvider._();

final class ProxyCountriesProvider
    extends $AsyncNotifierProvider<ProxyCountries, Map<String, String>> {
  ProxyCountriesProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'proxyCountriesProvider',
        isAutoDispose: false,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$proxyCountriesHash();

  @$internal
  @override
  ProxyCountries create() => ProxyCountries();
}

String _$proxyCountriesHash() => r'4a1f32682df0c185374ef8e377a595901a0161d3';

abstract class _$ProxyCountries extends $AsyncNotifier<Map<String, String>> {
  FutureOr<Map<String, String>> build();
  @$mustCallSuper
  @override
  void runBuild() {
    final ref =
        this.ref as $Ref<AsyncValue<Map<String, String>>, Map<String, String>>;
    final element =
        ref.element
            as $ClassProviderElement<
              AnyNotifier<AsyncValue<Map<String, String>>, Map<String, String>>,
              AsyncValue<Map<String, String>>,
              Object?,
              Object?
            >;
    element.handleCreate(ref, build);
  }
}
