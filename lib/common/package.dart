import 'dart:io';

import 'package:package_info_plus/package_info_plus.dart';

import 'common.dart';

extension PackageInfoExtension on PackageInfo {
  String get releaseLabel {
    final parts = version.split('.');
    if (parts.length != 3) return version;
    return '${parts[0]}.${parts[1]}.${parts[2].padLeft(2, '0')}';
  }

  String get ua => [
    '$appName/v$releaseLabel',
    'clash-verge',
    'Platform/${Platform.operatingSystem}',
  ].join(' ');
}
