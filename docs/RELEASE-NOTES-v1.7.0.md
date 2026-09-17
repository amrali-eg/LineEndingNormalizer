# LineEndingNormalizer v1.7.0

Fixes a real BOM-less UTF-32 misdetection gap left open by v1.6.0's guard, adds
two coverage/safety checks around `-Report`, and adds two new coverage
counters to the run summary. Everything else is repository hygiene.

It is a minor release rather than a patch because the CLI's summary output
gains two new lines and a new exit-3 refusal path, even though the UTF-32 fix
alone would have justified at least a patch on its own.

## Safety

- **BOM-less UTF-32 is now refused unconditionally**, closing a real gap in
  v1.6.0's guard. That guard refused UTF-32 only when the *opposite* byte
  order also strictly decoded the whole file — sufficient for UTF-16, but not
  for UTF-32: a BOM-less UTF-16 file with one character per line puts a C0
  control in every second code unit, so each resulting four-byte group is an
  in-range, unassigned UTF-32 scalar and the file misdetects as UTF-32LE,
  while the *opposite* UTF-32 order correctly fails to decode that same file.
  v1.6.0's opposite-order test therefore called such a file unambiguous and
  would have converted it, rewriting real UTF-16 content as if it were
  UTF-32 — silent corruption, not merely a wrong refusal. Every BOM-less
  UTF-32 file is now refused with reason code `UnprovableBomlessUtf32`
  (replacing `AmbiguousBomlessUtf32`), matching EncodingChecker's own
  historically-hardened policy for the same reason. See
  [SAFETY.md](SAFETY.md#bom-less-utf-32) for the full explanation, and
  [RELEASE-NOTES-v1.6.0.md](RELEASE-NOTES-v1.6.0.md) for the correction to
  that release's notes.

  **The cost is real:** ordinary, unambiguous BOM-less UTF-32 is refused too,
  with no override — LEN has no source-encoding flag, so there is no
  documented way through this refusal short of adding a byte-order mark.

- **An unwritable `-Report` path is now caught before any file is converted.**
  Previously, a missing parent directory or a path naming an existing
  directory was discovered only after every source file had already been
  converted, leaving the requested report absent regardless. This is now
  checked up front and returns exit code 3 without touching any file.

- **`-Report` is now written atomically.** The report file was previously
  truncated in place; an interrupted write could leave it empty or
  half-written and destroy whatever prior report was already there. It is now
  written to a temporary file and installed only once complete, reusing the
  same atomic-replace mechanism as file conversion.

## Reporting

- **Two new run-summary lines, shown only when nonzero:** `Dirs unreadable`
  (a directory LEN could not list) and `Dirs skipped` (a directory excluded by
  reserved name — `.git`, `bin`, `obj`, and similar). Previously an unreadable
  directory produced only a stderr warning and no counter, and a
  reserved-name skip was entirely silent — a run that missed part of the tree
  could report a clean summary and exit 0 with no trace of the coverage loss.
  `-DetectOnly` is unchanged; it has no summary to add these to.

## Internal

- Removed the unused `coverlet.collector` dependency (nothing in this repo
  invokes coverage collection) and bumped `Microsoft.NET.Test.Sdk` (17.14.1 →
  18.10.1) and `xunit.runner.visualstudio` (3.1.4 → 4.0.0, still xUnit v2
  compatible), matching the same bump already done in EncodingChecker.
  Test-tooling only.

## Repository hygiene

- Added `.editorconfig` (previously missing), matching EncodingChecker's:
  `.cs` is UTF-8 without a BOM, CRLF. All 14 tracked `.cs` files carried a BOM
  before this — LEN's actual standing convention, just undocumented — and
  were re-saved BOM-less to match the new rule. Each file lost exactly the
  3-byte BOM and nothing else.
- Fixed two stale/missing `.gitignore` rules found by comparing against EC:
  the publish-profile un-ignore paths still pointed at the pre-`sources/`-reorg
  location, and `**/.claude/settings.local.json` (machine-specific, never
  shared) was missing entirely.
- Added `AGENTS.md`, matching EncodingChecker's convention of keeping the same
  project instructions available to Codex and other generic agent tooling
  under both filenames.

## Unchanged

Conversion, backup, and replacement behavior for every case other than the
UTF-32 guard and `-Report` handling above are exactly what they were in
v1.6.0. All exit codes other than the new `-Report` preflight path, reason
codes other than the UTF-32 one, and CLI options are unchanged.

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
- `-DetectOnly` has no `-Report` preflight check and no directory-coverage
  counters; both are specific to the normalize/validate/`-WhatIf` path.
- The detector still evaluates entropy before honoring Unicode BOMs. Changing
  that order is deferred pending corpus testing across all three repositories.
- LEN's own `detector-parity.yml` compares `UnicodeDetector.cs` against
  EncodingChecker only; it does not compare `TextValidation.cs` and does not
  include CorpusTesters. A future divergence in `TextValidation.cs` alone
  would not be caught by this repository's CI.
