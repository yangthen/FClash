import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:fl_clash/common/common.dart';
import 'package:fl_clash/core/core.dart';
import 'package:fl_clash/enum/enum.dart';
import 'package:fl_clash/models/core.dart';

import 'interface.dart';
import 'pending_requests.dart';
import 'transport.dart';

class CoreService extends CoreHandlerInterface {
  static CoreService? _instance;

  late final IPCCoreTransport _transport;

  Completer<bool> _shutdownCompleter = Completer();

  final PendingRequests _requests = PendingRequests();
  late final Future<void> _ready;
  Future<void>? _starting;
  bool reattached = false;

  Process? _process;

  factory CoreService() {
    _instance ??= CoreService._internal();
    return _instance!;
  }

  CoreService._internal() {
    _ready = _initialize();
  }

  Future<void> _initialize() async {
    final identity = (await appPath.homeDirPath).toMd5();
    _transport = IPCCoreTransport(
      address: system.isWindows
          ? '\\\\.\\pipe\\FClashCore_$identity'
          : '/tmp/FClashCore_$identity.sock',
    );
    await _initServer();
  }

  Future<void> handleResult(ActionResult result) async {
    if (result.id?.isEmpty == true) {
      coreEventManager.sendEvent(CoreEvent.fromJson(result.data));
      return;
    }
    try {
      _requests.complete(result.id, await parasResult(result));
    } catch (error, stack) {
      _requests.fail(result.id, error, stack);
    }
  }

  Future<void> _initServer() async {
    await _transport.init();

    _transport.onDisconnect = () {
      _requests.disconnect();
      _handleInvokeCrashEvent();
      if (!_shutdownCompleter.isCompleted) {
        _shutdownCompleter.complete(true);
      }
    };

    _transport.dataStream
        .transform(uint8ListToListIntConverter)
        .transform(utf8.decoder)
        .listen(
          (data) async {
            try {
              final dataJson = await data.trim().commonToJSON<dynamic>();
              await handleResult(ActionResult.fromJson(dataJson));
            } catch (e) {
              commonPrint.log(
                'Failed to parse transport data: $e',
                logLevel: LogLevel.error,
              );
            }
          },
          onError: (error) {
            commonPrint.log(
              'Transport data stream error: $error',
              logLevel: LogLevel.error,
            );
          },
        );
  }

  void _handleInvokeCrashEvent() {
    coreEventManager.sendEvent(
      const CoreEvent(type: CoreEventType.crash, data: 'core done'),
    );
  }

  Future<void> start() async {
    if (_starting != null) return _starting;
    final starting = _start();
    _starting = starting;
    try {
      await starting;
    } finally {
      _starting = null;
    }
  }

  Future<void> _start() async {
    await _ready.timeout(const Duration(seconds: 10));
    try {
      await _transport.connectionCompleter.future.timeout(
        const Duration(seconds: 2),
      );
      reattached = true;
      return;
    } on TimeoutException {
      reattached = false;
    }
    if (_process != null) {
      await shutdown(false);
    }
    if (system.isWindows && await system.checkIsAdmin()) {
      final isSuccess = await request.startCoreByHelper(_transport.address);
      if (isSuccess) {
        await _transport.connectionCompleter.future.timeout(
          const Duration(seconds: 10),
        );
        return;
      }
    }
    try {
      _process = await Process.start(appPath.corePath, [
        _transport.address,
      ], mode: ProcessStartMode.detached);
    } catch (e) {
      commonPrint.log(
        'Failed to start core process: $e',
        logLevel: LogLevel.error,
      );
      _handleInvokeCrashEvent();
      rethrow;
    }
    await _transport.connectionCompleter.future.timeout(
      const Duration(seconds: 10),
    );
  }

  @override
  FutureOr<bool> destroy() async {
    await shutdown(false);
    await _transport.close();
    return true;
  }

  @override
  Future<void> detach() async {
    await invoke<bool>(method: ActionMethod.stopLog);
    await invoke<bool>(method: ActionMethod.requestEvents, data: false);
    _requests.disconnect();
    await _transport.close();
  }

  Future<void> sendMessage(String message) async {
    await _transport.connectionCompleter.future;
    _transport.send(message);
  }

  @override
  Future<bool> shutdown(bool isUser) async {
    _shutdownCompleter = Completer();
    if (_transport.connectionCompleter.isCompleted) {
      try {
        await invoke<bool>(
          method: ActionMethod.terminateCore,
          timeout: const Duration(seconds: 3),
        );
      } catch (error) {
        commonPrint.log('Core shutdown: $error');
      }
    }
    if (system.isWindows) {
      await request.stopCoreByHelper();
    }
    _transport.disconnected();
    _process?.kill();
    _process = null;
    _requests.disconnect();
    if (isUser) {
      return _shutdownCompleter.future.timeout(
        const Duration(seconds: 5),
        onTimeout: () => false,
      );
    } else {
      return true;
    }
  }

  @override
  Future<String> preload() async {
    await start();
    return '';
  }

  @override
  Future<T?> invoke<T>({
    required ActionMethod method,
    dynamic data,
    Duration? timeout,
  }) async {
    final id = '${method.name}#${utils.id}';
    return _requests.run<T?>(
      id,
      () =>
          sendMessage(json.encode(Action(id: id, method: method, data: data))),
      timeout: timeout ?? const Duration(seconds: 30),
    );
  }

  @override
  Completer get completer => _transport.connectionCompleter;
}

final coreService = system.isDesktop ? CoreService() : null;
