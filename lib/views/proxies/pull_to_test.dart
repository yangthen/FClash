import 'package:fl_clash/providers/app.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

/// Only vertical overscroll from the primary proxy list starts a manual test.
class ProxyPullToTest extends ConsumerWidget {
  final Future<void> Function() onTest;
  final Widget child;

  const ProxyPullToTest({super.key, required this.onTest, required this.child});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    if (!ref.watch(isMobileViewProvider)) return child;
    return RefreshIndicator(
      onRefresh: onTest,
      notificationPredicate: (notification) =>
          notification.depth == 0 && notification.metrics.axis == Axis.vertical,
      child: child,
    );
  }
}
