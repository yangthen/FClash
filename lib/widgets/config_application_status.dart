import 'package:fl_clash/common/common.dart';
import 'package:fl_clash/config/application.dart';
import 'package:fl_clash/providers/action.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

class ConfigApplicationStatus extends ConsumerWidget {
  const ConfigApplicationStatus({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = context.appLocalizations;
    return ValueListenableBuilder(
      valueListenable: configApplication.state,
      builder: (context, status, _) => ListTile(
        leading: Icon(
          status.phase == ConfigPhase.failed
              ? Icons.error_outline
              : Icons.settings,
        ),
        title: Text(switch (status.phase) {
          ConfigPhase.idle => l10n.configPending,
          ConfigPhase.applying => l10n.configApplying,
          ConfigPhase.applied => l10n.configApplied,
          ConfigPhase.failed => l10n.configFailed,
        }),
        subtitle: status.error == null ? null : Text(status.error.toString()),
        trailing: IconButton(
          tooltip: l10n.configApply,
          onPressed: status.phase == ConfigPhase.applying
              ? null
              : () => ref
                    .read(setupActionProvider.notifier)
                    .applyProfile(force: true),
          icon: const Icon(Icons.refresh),
        ),
      ),
    );
  }
}
