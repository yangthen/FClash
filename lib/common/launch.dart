import 'dart:async';
import 'dart:io';

import 'package:flutter/foundation.dart';
import 'package:launch_at_startup/launch_at_startup.dart';

import 'constant.dart';
import 'system.dart';

class AutoLaunch {
  static AutoLaunch? _instance;

  AutoLaunch._internal() {
    launchAtStartup.setup(
      // Preserve the existing startup registration when renaming the executable.
      appName: 'FlClash',
      appPath: Platform.resolvedExecutable,
    );
  }

  factory AutoLaunch() {
    _instance ??= AutoLaunch._internal();
    return _instance!;
  }

  Future<bool> get isEnable async {
    return launchAtStartup.isEnabled();
  }

  Future<bool> enable() async {
    return launchAtStartup.enable();
  }

  Future<bool> disable() async {
    return launchAtStartup.disable();
  }

  Future<void> updateStatus(bool isAutoLaunch) async {
    if (kDebugMode) {
      return;
    }
    if (isAutoLaunch == true) {
      await enable();
    } else if (await isEnable) {
      await disable();
    }
  }
}

final autoLaunch = system.isDesktop && !safeMode ? AutoLaunch() : null;
