import 'dart:async';
import 'dart:io';

import 'package:fl_clash/pages/error.dart';
import 'package:fl_clash/state.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:rust_api/rust_api.dart';
import 'package:window_manager/window_manager.dart';

import 'application.dart';
import 'common/common.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  try {
    if (system.isDesktop && !safeMode) {
      await RustLib.init();
    }
    final version = await system.version;
    final container = await globalState.init(version);
    HttpOverrides.global = FlClashHttpOverrides();
    runApp(
      UncontrolledProviderScope(
        container: container,
        child: const Application(),
      ),
    );
  } catch (e, s) {
    if (safeMode) {
      stderr.writeln('$e\n$s');
      await windowManager.ensureInitialized();
    }
    runApp(
      MaterialApp(
        home: InitErrorScreen(error: e, stack: s),
      ),
    );
    if (safeMode) await window?.show();
  }
}
