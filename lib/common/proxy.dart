import 'package:fl_clash/common/system.dart';
import 'constant.dart';
import 'package:proxy/proxy.dart';

final proxy = system.isDesktop && !safeMode ? Proxy() : null;
