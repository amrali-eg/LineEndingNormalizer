# LineEndingNormalizer project instructions

LEN is a Windows command-line tool that changes line endings without changing
the file's character encoding. Preserving file bytes outside line-ending
changes and reporting outcomes truthfully come before convenience.

## Where to start

- Application code is in `sources/LineEndingNormalizer/`.
- Tests are in `sources/LineEndingNormalizer.Tests/`.
- Read `docs/SAFETY.md` and `docs/DETECTION.md` before changing detection or
  normalization.
- Use `docs/CLI.md` as the switch and exit-code contract; verify its claims
  against `Program.cs`.
- Keep `README.md` introductory. Put technical detail in focused `docs/` files.

## Safety rules

- LEN changes line endings only. Do not transcode files or normalize Unicode,
  whitespace, case, or any text other than recognized line separators.
- Unicode input must be decoded and encoded strictly. Never accept replacement
  characters, best-fit substitutions, or incomplete trailing input silently.
- Preserve the original encoding and BOM state.
- For approved legacy encodings, preserve every byte except verified CR and LF
  bytes. Do not introduce character-level reinterpretation into that path.
- Keep the conservative BOM-less UTF-16 rule: convert only when the opposite
  byte order is structurally impossible. Otherwise refuse without writing.
- Write to a temporary sibling, verify it, and recheck the destination before
  replacement. Do not weaken source-change, reparse-point, metadata, or atomic
  installation checks as incidental cleanup.
- `-WhatIf`, `-ValidateOnly`, and `-DetectOnly` must never modify source files.
- A safe refusal is not a processing failure. Preserve stable result names,
  reason codes, diagnostics, and documented exit-code precedence.
- `-Backup` must contain the exact original bytes used for conversion and pass
  SHA-256 verification before installation proceeds.
- Reject or skip linked roots, files, and directories consistently in every
  mode. Never follow reparse points during a scan or conversion.
- Never modify corpus fixtures in place. Destructive checks use disposable
  working copies.

## Keep LEN distinct from EncodingChecker

- LEN has no GUI, source-encoding override, conversion plan, apply mode,
  conversion journal, or built-in restore command. Do not import those concepts
  unless the user explicitly requests a product change.
- EncodingChecker may decode and transcode text after choosing a source codec;
  LEN must preserve the source encoding and only normalize line endings.
- Prefer a small, demonstrated fix over speculative confidence rules, full-file
  buffering, or unrelated batch-transaction machinery.

## Shared detector boundary

`UnicodeDetector.cs` and `TextValidation.cs` are compared across LEN,
EncodingChecker, and CorpusTesters by `.github/workflows/detector-parity.yml`.
Do not modify them without explicit approval to change the shared detector.
`TextEncoding.cs` is an application wrapper and need not be byte-identical.

Do not weaken the parity check to accommodate an accidental difference, add
UTF-7 detection unasked, or update `UTF.Unknown` as an ordinary dependency bump.
An approved detector or detector-dependency change requires comparison with EC
and CorpusTesters, paired before/after results, parity checks, and the relevant
corpus regression audit. Do not edit sibling repositories without authorization.

## Build and test

Commands below run from the **repository root**, not `sources`, with Windows and the
.NET 10 SDK:

```powershell
dotnet build sources/LineEndingNormalizer.slnx --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Build failed; do not run old binaries.' }
dotnet test sources/LineEndingNormalizer.slnx --configuration Release --no-build
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
```

- Run focused tests during development and the full suite before handoff for
  code changes. Documentation-only edits do not need an application rebuild.
- The test assembly deliberately disables parallel test classes because some
  end-to-end tests redirect process-wide console output. Do not re-enable it
  without removing that shared-state race.
- Test real public or orchestration paths where practical. For preview,
  refusal, cancellation, and failure cases, assert the source bytes remain
  unchanged.
- When changing failure handling, force that failure path. Passing normal runs
  do not verify it.
- Some tests create real NTFS junctions and require Windows behavior.

## Release evidence

- Follow `docs/RELEASE-CHECKLIST.md` before publishing.
- Verify the project version, manifest version, and release tag agree.
- Required checks include a warning-free Release build, the full test suite,
  detector parity, and the documented manual smoke cases.
- Detection or normalization-policy changes require corpus regression evidence.
  Compare outcome distributions; complete row counts alone cannot prove correct
  classification.
- Record the exact commit and executable or assembly hash for release evidence.
  Do not carry evidence from an older build forward as if it measured a new one.

## File and documentation conventions

- Follow `.gitattributes`: C# and Windows scripts use CRLF; shell scripts use LF.
- Preserve the existing encoding and line-ending style of touched files. Check
  raw bytes for mixed endings or BOM differences rather than relying on a
  normalized diff.
- Do not perform repository-wide formatting, encoding, or line-ending cleanup
  as part of an unrelated change.
- Keep comments concise and explain why. Tests may use fuller comments to state
  the regression or safety property they prevent.
- Write documentation for a person: lead with behavior and consequences, then
  name implementation details only when they help verification.
- Do not hardcode current package versions, test totals, or release numbers in
  this file.
