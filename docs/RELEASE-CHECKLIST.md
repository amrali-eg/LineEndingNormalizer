# Release checklist

Use this checklist for every published LEN build.

## Source and version

- [ ] Working tree is clean.
- [ ] `LineEndingNormalizer.csproj` contains the intended version.
- [ ] `app.manifest` contains the same four-part version.
- [ ] Release tag is exactly `v<project version>`.
- [ ] README and release notes describe any changed refusal or safety rule.

The release workflow rejects a tag whose version differs from the project or
manifest.

## Automated gates

- [ ] Release build succeeds with no warnings.
- [ ] Full test suite passes.
- [ ] Empty include/exclude arguments are rejected.
- [ ] Linked roots, files, and directories are rejected or skipped as designed.
- [ ] BOM-less ambiguous UTF-16 fixture is refused without modifying bytes.
- [ ] Unambiguous BOM-less UTF-16 fixture still normalizes correctly.
- [ ] Backup SHA-256 matches the original source bytes.
- [ ] Report errors contain a stable `ReasonCode` and useful `Diagnostic`.
- [ ] Detector parity passes across LEN, EncodingChecker, and CorpusTesters.

## Manual smoke test

Use a temporary folder and record the commit, executable SHA-256, Windows and
.NET versions, commands, expected results, and observed hashes.

- [ ] `-WhatIf` reports changes and writes nothing.
- [ ] Normal conversion produces exact expected line endings.
- [ ] `-Backup` creates the original bytes and conversion succeeds.
- [ ] Forced backup failure leaves the source unchanged and reports why.
- [ ] Ambiguous BOM-less UTF-16 is refused and remains byte-identical.
- [ ] A linked file is skipped in normalize, preview, validate, and detect modes.
- [ ] Ctrl+C leaves no partially written source file.

## Publish

- [ ] Publish both framework-dependent and self-contained artifacts.
- [ ] If signing is configured, verify signatures on both executables.
- [ ] Verify archive names and GitHub SHA-256 digests.
- [ ] Link any audit evidence and state its limits in the release notes.
