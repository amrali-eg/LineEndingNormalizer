# LineEndingNormalizer v1.6.0

**Correction (added in v1.7.0):** the Safety section below describes and this
version genuinely ships an opposite-byte-order ambiguity test for BOM-less
UTF-32, reporting `AmbiguousBomlessUtf32`. That test is insufficient: a
BOM-less UTF-16 file can misdetect as UTF-32 and pass this check, because the
opposite UTF-32 order need not fail to decode a file that is not really
UTF-32 at all. **v1.6.0 as published can convert such a file, silently
corrupting it.** v1.7.0 replaces this with an unconditional refusal
(`UnprovableBomlessUtf32`) that closes the gap; see
[SAFETY.md](SAFETY.md#bom-less-utf-32) and
[RELEASE-NOTES-v1.7.0.md](RELEASE-NOTES-v1.7.0.md). This file is left
otherwise unedited as a historical record of what was intended at the time.

Adds one narrow new safety refusal, closes a test-coverage gap in the existing
backup verification, updates the legacy-detection dependency, and reorganizes
the repository into the same `sources/` layout EncodingChecker already uses.
No detection, normalization, backup, or reporting behavior most users will
ever observe has changed.

It is a minor release rather than a patch because a new refusal reason code
is added to the CLI's reported output, even though it is not expected to fire
in practice — see **Safety** below.

## Safety

- **Ambiguous BOM-less UTF-32 is now refused, mirroring UTF-16.** LEN already
  refused a BOM-less UTF-16 file when the opposite byte order also strictly
  decoded the whole file. `UnicodeDetector.CheckUtf32` has the identical
  ambiguity shape, but nothing guarded it. LEN now performs the same
  opposite-order full-file decode for UTF-32 and refuses with the new reason
  code `AmbiguousBomlessUtf32` when both orders succeed.

  **In practice, this refusal is close to theoretical.** Every line separator
  LEN recognizes (CR, LF, NEL, LS, PS) is a small scalar value, and a 4-byte
  UTF-32 group that encodes one of them always pushes the *opposite* byte
  order's value for that same group outside the valid `U+0000`-`U+10FFFF`
  range. A file therefore cannot both need line-ending conversion and be
  genuinely ambiguous between the two byte orders at the same time. The guard
  exists for the same reason its UTF-16 counterpart does — closing an
  asymmetry and guarding against a future change to the separator set or
  decode semantics — not because a real file is expected to trigger it. See
  [SAFETY.md](SAFETY.md) for the full explanation.

- **The backup SHA-256 mismatch refusal now has regression coverage.** The
  check itself is unchanged — it has existed since v1.5.0 — but nothing
  forced the mismatch and asserted the refusal, leaving the repository's most
  safety-critical untested path untested. New tests exercise both
  `CreateBackup` and `VerifyFileSha256`'s refusal directly.

## Detector dependency

`UTF.Unknown` is updated from 2.6.0 to 2.7.0. `TextEncoding.cs` now calls its
new `DetectFromBytes(ReadOnlySpan<byte>)` overload directly instead of first
copying the detection sample into a `byte[]`. Detection policy is unchanged;
`UnicodeDetector.cs` and `TextValidation.cs` were not modified.

A comment beside the control/private-use character check in
`TextValidation.cs` is corrected: it said these characters are "ignored" in
the printable-text ratio, when they in fact lower it (they count toward the
total but never toward the printable count). Comment-only; the calculation is
untouched. The same fix landed identically in EncodingChecker and
CorpusTesters, keeping this parity-checked file byte-identical across all
three.

A four-corpus regression audit (5,078 files) run against EncodingChecker,
which embeds the same detector, found zero changed detection outcomes from
the dependency bump. LEN inherits that evidence through detector parity
rather than through its own corpus run — see [SAFETY-AUDIT.md](SAFETY-AUDIT.md).

## Internal

- The repository is reorganized: the application moves to
  `sources/LineEndingNormalizer/`, tests to
  `sources/LineEndingNormalizer.Tests/`, and the solution to
  `sources/LineEndingNormalizer.slnx` — matching EncodingChecker's layout.
  Every moved file was verified as a 100%-identical git rename; nothing
  changed but the path. Build and test commands now run
  `sources/LineEndingNormalizer.slnx` from the repository root; see
  [CLAUDE.md](../CLAUDE.md).
- Two small duplications were removed for readability: `Program.cs`'s
  WhatIf/ValidateOnly mode-line printing, and a duplicated temporary-file-name
  assembly in `LosslessFileWriter.cs` (`CreateBackup` and `ConvertFile` now
  share one `BuildTempFileName` helper). Neither changes any observable
  behavior.

## Unchanged

Conversion, detection, normalization, backup, and replacement behavior for
every case other than the new UTF-32 guard above are exactly what they were
in v1.5.0. All exit codes, reason codes, report columns, and CLI options are
unchanged.

## Known limits

- No restore command exists. A `.bak` is independently hash-verified recovery
  data, not a restore feature.
- The backup is created before the source is replaced, so a later failure can
  leave a valid `.bak` beside an unchanged source. This is deliberate.
- The final destination check narrows, but does not eliminate, the race between
  checking a file and replacing it.
- CSV fields are RFC 4180 quoted but are not neutralized against spreadsheet
  formula interpretation.
- Establishing BOM-less UTF-16 or UTF-32 safety costs a second complete read
  of the file.
- The detector still evaluates entropy before honoring Unicode BOMs. Changing
  that order is deferred pending corpus testing across all three repositories.
- LEN's own `detector-parity.yml` compares `UnicodeDetector.cs` against
  EncodingChecker only; it does not compare `TextValidation.cs` and does not
  include CorpusTesters. A future divergence in `TextValidation.cs` alone
  would not be caught by this repository's CI.
