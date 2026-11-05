import 'package:fl_clash/widgets/country_flag.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets('renders country flag with tooltip and bundled emoji font', (
    tester,
  ) async {
    await tester.pumpWidget(
      const MaterialApp(home: CountryFlag(countryCode: 'jp')),
    );
    final text = tester.widget<Text>(find.text('🇯🇵'));
    expect(text.style?.fontFamily, 'Twemoji');
    expect(text.semanticsLabel, 'JP');
    expect(find.byTooltip('JP'), findsOneWidget);
  });

  testWidgets('invalid country codes do not render a flag', (tester) async {
    await tester.pumpWidget(
      const MaterialApp(home: CountryFlag(countryCode: 'UNKNOWN')),
    );
    expect(find.byType(Text), findsNothing);
  });
}
