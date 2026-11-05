import 'package:fl_clash/enum/enum.dart';
import 'package:flutter/material.dart';

class CountryFlag extends StatelessWidget {
  final String countryCode;

  const CountryFlag({super.key, required this.countryCode});

  @override
  Widget build(BuildContext context) {
    final code = countryCode.toUpperCase();
    if (!RegExp(r'^[A-Z]{2}$').hasMatch(code)) return const SizedBox.shrink();
    final flag = String.fromCharCodes(
      code.codeUnits.map((letter) => letter - 0x41 + 0x1F1E6),
    );
    return Tooltip(
      message: code,
      child: Text(
        flag,
        semanticsLabel: code,
        style: TextStyle(
          fontFamily: FontFamily.twEmoji.value,
          fontSize: 16,
          height: 1,
        ),
      ),
    );
  }
}
