import 'dart:async';

import 'package:fl_clash/providers/app.dart';
import 'package:fl_clash/views/proxies/pull_to_test.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

Future<void> mount(
  WidgetTester tester, {
  bool mobile = true,
  bool horizontal = false,
  bool grid = false,
  int items = 1,
  required Future<void> Function() onTest,
}) async {
  await tester.pumpWidget(
    ProviderScope(
      overrides: [isMobileViewProvider.overrideWithValue(mobile)],
      child: MaterialApp(
        home: Scaffold(
          body: ProxyPullToTest(
            onTest: onTest,
            child: grid
                ? GridView.builder(
                    physics: const AlwaysScrollableScrollPhysics(),
                    gridDelegate:
                        const SliverGridDelegateWithFixedCrossAxisCount(
                          crossAxisCount: 2,
                        ),
                    itemCount: items,
                    itemBuilder: (_, index) => Text('proxy $index'),
                  )
                : ListView.builder(
                    scrollDirection: horizontal
                        ? Axis.horizontal
                        : Axis.vertical,
                    physics: const AlwaysScrollableScrollPhysics(),
                    itemExtent: 80,
                    itemCount: items,
                    itemBuilder: (_, index) => Text('proxy $index'),
                  ),
          ),
        ),
      ),
    ),
  );
}

void main() {
  testWidgets('short proxy grid supports pulling at the top', (tester) async {
    var calls = 0;
    await mount(tester, grid: true, onTest: () async => calls++);
    await tester.drag(find.byType(GridView), const Offset(0, 350));
    await tester.pumpAndSettle();
    expect(calls, 1);
  });

  testWidgets(
    'short mobile list pulls to test and retains progress until done',
    (tester) async {
      final completion = Completer<void>();
      var calls = 0;
      await mount(
        tester,
        onTest: () {
          calls++;
          return completion.future;
        },
      );
      await tester.drag(find.byType(ListView), const Offset(0, 350));
      await tester.pump();
      await tester.pump(const Duration(seconds: 1));
      expect(calls, 1);
      expect(find.byType(RefreshProgressIndicator), findsOneWidget);
      await tester.drag(find.byType(ListView), const Offset(0, 350));
      await tester.pump(const Duration(seconds: 1));
      expect(calls, 1);
      completion.complete();
      await tester.pumpAndSettle();
      expect(find.byType(RefreshProgressIndicator), findsNothing);
      await tester.drag(find.byType(ListView), const Offset(0, 350));
      await tester.pumpAndSettle();
      expect(calls, 2);
    },
  );

  testWidgets('scrolling in middle of a long list does not trigger a test', (
    tester,
  ) async {
    var calls = 0;
    await mount(tester, items: 50, onTest: () async => calls++);
    await tester.drag(find.byType(ListView), const Offset(0, -600));
    await tester.pumpAndSettle();
    await tester.drag(find.byType(ListView), const Offset(0, 150));
    await tester.pumpAndSettle();
    expect(calls, 0);
  });

  testWidgets('desktop view does not add a pull gesture', (tester) async {
    var calls = 0;
    await mount(tester, mobile: false, onTest: () async => calls++);
    expect(find.byType(RefreshIndicator), findsNothing);
    await tester.drag(find.byType(ListView), const Offset(0, 350));
    await tester.pumpAndSettle();
    expect(calls, 0);
  });

  testWidgets('horizontal group switching does not trigger a test', (
    tester,
  ) async {
    var calls = 0;
    await mount(tester, horizontal: true, onTest: () async => calls++);
    await tester.drag(find.byType(ListView), const Offset(350, 0));
    await tester.pumpAndSettle();
    expect(calls, 0);
  });
}
