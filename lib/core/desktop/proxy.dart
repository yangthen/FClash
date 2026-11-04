import 'package:fl_clash/common/common.dart';
import 'package:fl_clash/core/operation_queue.dart';
import 'package:fl_clash/models/models.dart';

class DesktopProxyCoordinator {
  final _queue = OperationQueue();
  bool _closing = false;

  Future<void> update(ProxyState state) {
    if (_closing) return Future.value();
    return _queue.run(() async {
      if (_closing) return;
      final result = state.isStart && state.systemProxy
          ? await proxy?.startProxy(state.port, state.bassDomain)
          : await proxy?.stopProxy();
      if (result == false) throw StateError('System proxy update failed');
    });
  }

  Future<void> close({required bool stop}) {
    _closing = true;
    return _queue.run(() async {
      if (stop && await proxy?.stopProxy() == false) {
        throw StateError('System proxy cleanup failed');
      }
    });
  }
}

final desktopProxy = DesktopProxyCoordinator();
