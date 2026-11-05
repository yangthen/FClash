import 'package:fl_clash/common/measure.dart';
import 'package:fl_clash/common/theme.dart';
import 'package:fl_clash/enum/enum.dart';
import 'package:fl_clash/models/models.dart';
import 'package:fl_clash/providers/providers.dart';
import 'package:fl_clash/state.dart';
import 'package:fl_clash/views/proxies/card.dart';
import 'package:fl_clash/views/proxies/common.dart';
import 'package:fl_clash/widgets/country_flag.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  const proxy = Proxy(
    name: 'JMS-1491941@c67s2.portablesubmarines.com:443',
    type: 'Vless',
  );

  for (final type in ProxyCardType.values) {
    testWidgets('${type.name} card keeps cached flag at the trailing edge', (
      tester,
    ) async {
      final container = ProviderContainer(
        overrides: [
          selectedProxyNameProvider('group').overrideWithValue(null),
          proxyDescProvider(proxy).overrideWithValue('Vless'),
          delayProvider(
            proxyName: proxy.name,
            testUrl: null,
          ).overrideWithValue(257),
          realSelectedProxyStateProvider(
            proxy.name,
          ).overrideWithValue(SelectedProxyState(proxyName: proxy.name)),
          currentProfileIdProvider.overrideWithBuild((_, _) => 1),
          proxyCountriesProvider.overrideWithBuild(
            (_, _) async => {proxyCountryKey(1, proxy.name): 'JP'},
          ),
        ],
      );
      addTearDown(container.dispose);
      await tester.pumpWidget(
        UncontrolledProviderScope(
          container: container,
          child: MaterialApp(
            home: Builder(
              builder: (context) {
                globalState.measure = Measure.of(context, 1);
                globalState.theme = CommonTheme.of(context, 1);
                return Center(
                  child: SizedBox(
                    width: 280,
                    height: getItemHeight(type),
                    child: ProxyCard(
                      groupName: 'group',
                      testUrl: null,
                      proxy: proxy,
                      groupType: GroupType.Selector,
                      type: type,
                    ),
                  ),
                );
              },
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(tester.takeException(), isNull);
      expect(find.byType(CountryFlag), findsOneWidget);
      final cardRect = tester.getRect(find.byType(ProxyCard));
      final flagRect = tester.getRect(find.byType(CountryFlag));
      expect(flagRect.right, closeTo(cardRect.right - 12, 1));
      expect(flagRect.center.dy, greaterThan(cardRect.center.dy));
      expect(find.text('257 ms'), findsOneWidget);
    });
  }
}
