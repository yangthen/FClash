import 'package:flutter_test/flutter_test.dart';
import '../scripts/release_version.dart';

void main() {
  test('release labels carry at 100 and restart patch at 01', () {
    expect(ReleaseVersion.parse('0.8.94').next.label, '0.8.95');
    expect(ReleaseVersion.parse('0.8.99').next.label, '0.9.01');
    expect(ReleaseVersion.parse('0.9.99').next.label, '0.10.01');
    expect(ReleaseVersion.parse('0.99.99').next.label, '1.0.01');
    expect(ReleaseVersion.parse('0.8.99').next.semver, '0.9.1');
    expect(() => ReleaseVersion.parse('0.8.100'), throwsFormatException);
  });
}
