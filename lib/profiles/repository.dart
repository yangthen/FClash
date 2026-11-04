import 'dart:io';
import 'dart:typed_data';
import 'package:fl_clash/common/common.dart';
import 'package:fl_clash/core/controller.dart';
import 'package:fl_clash/models/profile.dart';

extension ProfileStorage on Profile {
  Future<Profile?> checkAndUpdateAndCopy() async {
    final mFile = await _getFile(false);
    final isExists = await mFile.exists();
    if (isExists || url.isEmpty) {
      return null;
    }
    return update();
  }

  Future<File> _getFile([bool autoCreate = true]) async {
    final path = await appPath.getProfilePath(id.toString());
    final file = File(path);
    final isExists = await file.exists();
    if (!isExists && autoCreate) {
      return file.create(recursive: true);
    }
    return file;
  }

  Future<File> get file async {
    return _getFile();
  }

  Future<Profile> update() async {
    final response = await request.getFileResponseForUrl(url);
    final disposition = response.headers.value('content-disposition');
    final userinfo = response.headers.value('subscription-userinfo');
    return copyWith(
      label: label.takeFirstValid([
        utils.getFileNameForDisposition(disposition),
        id.toString(),
      ]),
      subscriptionInfo: SubscriptionInfo.formHString(userinfo),
    ).saveFile(response.data ?? Uint8List.fromList([]));
  }

  Future<Profile> saveFile(Uint8List bytes) async {
    final path = await appPath.tempFilePath;
    final tempFile = File(path);
    try {
      await tempFile.safeWriteAsBytes(bytes);
      final message = await coreController.validateConfig(path);
      if (message.isNotEmpty) throw StateError(message);
      final target = await file;
      await tempFile.copy(target.path);
    } finally {
      await tempFile.safeDelete();
    }
    return copyWith(lastUpdateDate: DateTime.now());
  }

  Future<Profile> saveFileWithPath(String path) async {
    final message = await coreController.validateConfig(path);
    if (message.isNotEmpty) {
      throw message;
    }
    final mFile = await file;
    await File(path).copy(mFile.path);
    return copyWith(lastUpdateDate: DateTime.now());
  }
}
