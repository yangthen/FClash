import 'dart:convert';
import 'dart:io';

class ReleaseVersion {
  final int major;
  final int minor;
  final int patch;

  const ReleaseVersion(this.major, this.minor, this.patch);

  factory ReleaseVersion.parse(String value) {
    final match = RegExp(r'^(\d+)\.(\d+)\.(\d{1,2})$').firstMatch(value);
    if (match == null) {
      throw FormatException('Expected major.minor.patch', value);
    }
    final version = ReleaseVersion(
      int.parse(match[1]!),
      int.parse(match[2]!),
      int.parse(match[3]!),
    );
    if (version.minor >= 100 || version.patch < 1 || version.patch >= 100) {
      throw FormatException('Minor must be 0–99 and patch 01–99', value);
    }
    return version;
  }

  String get label => '$major.$minor.${patch.toString().padLeft(2, '0')}';
  String get semver => '$major.$minor.$patch';

  ReleaseVersion get next {
    if (patch < 99) return ReleaseVersion(major, minor, patch + 1);
    if (minor < 99) return ReleaseVersion(major, minor + 1, 1);
    return ReleaseVersion(major + 1, 0, 1);
  }
}

void main(List<String> args) {
  if (args.length != 1) {
    stderr.writeln('Usage: dart scripts/release_version.dart 0.8.99');
    exitCode = 64;
    return;
  }
  final next = ReleaseVersion.parse(args.single).next;
  stdout.writeln(jsonEncode({'label': next.label, 'pubspec': next.semver}));
}
