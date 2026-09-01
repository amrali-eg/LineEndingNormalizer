# LineEndingNormalizer v1.5.0

This release refuses conversions it cannot show to be safe, verifies backups
against the exact bytes a conversion read, and reports a refusal as a refusal
rather than a failure.

It is a minor release rather than a patch because two published CLI behaviours
change. See **Breaking changes** before upgrading a script.

## Breaking changes

- **A safe refusal now exits `5`.** LEN previously reported an ambiguous
  BOM-less UTF-16 file as a processing error with `Result=Error` and exit `3`.
  Nothing had gone wrong: LEN worked correctly and declined to rewrite the file.
  The report now says `Result=Refused`, and the run exits `5` — the same meaning
  EncodingChecker gives that code, so a script driving both tools can share one
  mapping.
- **A missing `-BasePath` directory now exits `1`.** It previously exited `5`.
  A directory that does not exist is an invalid invocation, not a refusal, and
  `5` is now reserved for the safety meaning above.

When more than one outcome applies, the first of these wins:

```text
3  a file failed, or the report could not be written
5  a file was safely refused
2  -FailOnChanges found files requiring conversion
0  clean run
```

A refusal outranks `-FailOnChanges` because a refused file is one whose
conversion status `-FailOnChanges` cannot speak for.

## Safety

- **Ambiguous BOM-less UTF-16 is refused.** Without a byte-order mark, UTF-16
  bytes usually decode as valid text in *both* byte orders, so the file cannot
  say which is right. LEN now strictly decodes the whole file under the opposite
  byte order and refuses when that also succeeds, with reason code
  `AmbiguousBomlessUtf16`. The check runs before `-WhatIf` reports a file as
  convertible and before any metadata capture, backup, or write.

  **Expect most BOM-less UTF-16 files to be refused.** Byte-swapped Latin text
  lands in a valid CJK range, so ordinary ASCII content decodes cleanly either
  way. Conversion proceeds only where the opposite byte order is structurally
  impossible. This prevents a reproduced corruption case where UTF-16BE bytes
  read as UTF-16LE had CR inserted before text that only resembled LF under the
  wrong byte order.

- **Backups are verified against the bytes the conversion read.** The backup is
  copied, flushed, and its SHA-256 compared with the hash captured while
  converting, then checked again after installation. A mismatch aborts before
  the source is replaced, with reason code `BackupVerificationFailed`. A source
  that changed between the conversion read and the backup is therefore caught.

- **Linked files are skipped in every mode**, including `-WhatIf`,
  `-ValidateOnly`, and `-DetectOnly`. Linked directories were already skipped.

- **Repeated leading BOMs are preserved.** The first is the encoding signature;
  a second is `U+FEFF` text content. Both survive, and only line endings change.

## Command line

- Explicitly supplied but empty filters are rejected rather than silently
  meaning every file: `-Include ""`, `-Include ",,,"`, `-Include "   "`, and the
  `-Exclude` equivalents. Omitting `-Include` still defaults to `*`.
- Contradictory mode combinations are rejected rather than silently ignored —
  `-Verbose` with `-Quiet`, `-WhatIf` with `-Backup`, and the `-DetectOnly` and
  `-ValidateOnly` conflicts listed in [CLI.md](CLI.md).
- `--version` prints the version and exits. It needs no other argument and no
  `-BasePath`, so a release check can read the version from the built binary.

## Reports

Normalization reports gain a machine-readable reason:

```text
File,Encoding,BOM,LineEnding,Target,Result,ReasonCode,Diagnostic
```

`ReasonCode` is intended for scripts; `Diagnostic` keeps the readable message.
Current values are `AmbiguousBomlessUtf16`, `BackupVerificationFailed`,
`InvalidEncoding`, `AccessDenied`, `IoError`, and `UnexpectedError`. Detected
encoding information is retained when an error occurs after detection, rather
than being discarded.

## Release integrity

The release workflow now fails unless the git tag, the project version, and the
application manifest agree. The manifest is `1.5.0.0` for project version
`1.5.0`.

## Unchanged

The shared detector is untouched: `TextValidation.cs` and `UnicodeDetector.cs`
are byte-identical to the previous release, and detector parity with
EncodingChecker and CorpusTesters still passes.

## Known limits

- No restore command exists. A `.bak` is independently hash-verified recovery
  data, not a restore feature.
- The backup is created before the source is replaced, so a later failure can
  leave a valid `.bak` beside an unchanged source. This is deliberate.
- The final destination check narrows, but does not eliminate, the race between
  checking a file and replacing it.
- CSV fields are RFC 4180 quoted but are not neutralized against spreadsheet
  formula interpretation.
- Establishing BOM-less UTF-16 safety costs a second complete read of the file.
- The detector still evaluates entropy before honoring Unicode BOMs. Changing
  that order is deferred pending corpus testing across all three repositories.
