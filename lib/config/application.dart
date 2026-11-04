import 'dart:async';
import 'package:flutter/foundation.dart';

enum ConfigPhase { idle, applying, applied, failed }

@immutable
class ConfigApplicationState {
  final ConfigPhase phase;
  final String? digest;
  final Object? error;

  const ConfigApplicationState(this.phase, {this.digest, this.error});
}

class ConfigApplication {
  final state = ValueNotifier(const ConfigApplicationState(ConfigPhase.idle));
  String? _appliedDigest;
  int _revision = 0;

  int request() {
    _revision++;
    state.value = const ConfigApplicationState(ConfigPhase.applying);
    return _revision;
  }

  void markPending() {
    _revision++;
    state.value = const ConfigApplicationState(ConfigPhase.idle);
  }

  void adopt(String digest) {
    _appliedDigest = digest;
    state.value = ConfigApplicationState(ConfigPhase.applied, digest: digest);
  }

  bool isCurrent(int revision) => revision == _revision;

  void invalidate() {
    _appliedDigest = null;
    _revision++;
    state.value = const ConfigApplicationState(ConfigPhase.idle);
  }

  Future<bool> apply({
    required int revision,
    required String digest,
    required Future<void> Function() operation,
    bool force = false,
  }) async {
    if (!isCurrent(revision)) return false;
    if (!force && digest == _appliedDigest) {
      state.value = ConfigApplicationState(ConfigPhase.applied, digest: digest);
      return false;
    }
    state.value = ConfigApplicationState(ConfigPhase.applying, digest: digest);
    try {
      await operation();
      if (isCurrent(revision)) {
        _appliedDigest = digest;
        state.value = ConfigApplicationState(
          ConfigPhase.applied,
          digest: digest,
        );
      }
      return true;
    } catch (error) {
      if (isCurrent(revision)) {
        state.value = ConfigApplicationState(
          ConfigPhase.failed,
          digest: digest,
          error: error,
        );
      }
      rethrow;
    }
  }
}

final configApplication = ConfigApplication();
