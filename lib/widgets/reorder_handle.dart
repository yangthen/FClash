import 'package:fl_clash/common/common.dart';
import 'package:flutter/material.dart';

class ReorderHandle extends StatelessWidget {
  final int index;
  final bool enabled;

  const ReorderHandle({super.key, required this.index, this.enabled = true});

  @override
  Widget build(BuildContext context) {
    return ReorderableDragStartListener(
      index: index,
      enabled: enabled,
      child: Tooltip(
        message: context.appLocalizations.dragToReorder,
        child: MouseRegion(
          cursor: enabled ? SystemMouseCursors.grab : SystemMouseCursors.basic,
          child: SizedBox(
            width: 48,
            height: 48,
            child: Icon(
              Icons.drag_handle,
              color: context.colorScheme.onSurfaceVariant,
            ),
          ),
        ),
      ),
    );
  }
}
