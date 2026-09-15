---
name: lineendingnormalizer-forensic-review
description: >
  Perform an evidence-driven forensic review of the LineEndingNormalizer
  C#/.NET repository. Use for whole-repository audits, release-readiness
  reviews, hidden regressions, line-ending or encoding preservation,
  Unicode/BOM handling, false passes or failures, diagnostic accuracy,
  concurrency, backup safety, test gaps, or independent review from scratch.
  Prioritize reproducible correctness defects over style or broad refactoring.
---

# LineEndingNormalizer Forensic Review

Review LEN as an in-place file-rewriting tool whose narrow promise is:

> Change recognized line endings without changing the file's encoding or any
> unrelated text or bytes.

Determine whether LEN can incorrectly detect, normalize, verify, replace, back
up, or report files. Give special weight to silent data changes, false success,
false failure, and diagnostics that blame the wrong cause.

Do not optimize for the number of findings. Prefer a few demonstrated defects
to many speculative observations.

## Review priorities

Review in this order:

1. Silent data corruption or loss
2. False-success paths
3. Encoding and BOM preservation
4. Line-ending normalization correctness
5. Output and backup verification
6. Temporary-file and replacement safety
7. Detection, validation, and refusal correctness
8. Exception propagation and diagnostics
9. CLI modes, reports, and exit-code truthfulness
10. Concurrency, cancellation, and complete result accounting
11. Regression-test quality
12. Maintainability and performance with credible correctness impact

Style-only findings are out of scope unless they create a concrete risk.

## 1. Trace the real pipeline first

Trace findings through the complete flow:

`bytes -> detection -> strict validation -> line-ending classification ->`
`normalization -> temporary output -> verification -> destination recheck ->`
`metadata application -> optional verified backup -> replacement -> report and exit code`

Identify scan and selection entry points, Unicode and legacy detection routes,
full-file validation, both normalization paths, BOM handling, temporary output,
verification, backup binding, replacement, CLI modes, concurrent result
collection, and relevant tests. Do not review a helper without checking how its
callers narrow or reinterpret its result.

## 2. Core invariants

LEN must preserve:

- the source encoding and BOM state;
- every Unicode scalar except recognized line separators changed to the target;
- every legacy byte except verified CR and LF bytes;
- exact file content when no normalization is needed.

LEN must not silently transcode, use replacement fallback, drop malformed or
truncated input, normalize unrelated text, or report invalid input as verified
unchanged.

Detection of a sample is not full-file validation. Syntactic validity is not
proof of the intended encoding or byte order. Preview and validation modes must
exercise enough of the real safety policy not to promise a write that conversion
must later refuse.

Every processed file must receive one truthful outcome. On cancellation,
distinguish completed work from files not attempted, and check that summaries
and reports do not imply the whole candidate set was processed. Preview results
describe what LEN would do and must not be reported as completed writes.

## 3. Line-ending correctness

Test CRLF, LF, CR, mixed endings, empty files, files without line endings, a
final CR, consecutive separators, and each target style. Include CR at the end
of one buffer with LF at the start of the next. For Unicode, include NEL, LS,
and PS. For multibyte encodings, check characters whose encoded bytes contain
`0x0D` or `0x0A` internally.

Verify final bytes rather than labels or counts. For Unicode, compare strict
scalar sequences after accounting only for the requested separator change.
NEL, LS, and PS can require normalization even though `LineEndingKind` reports
only the conventional CR, LF, and CRLF families; do not treat that reporting
choice as proof that no conversion is needed.

## 4. Encoding review

### UTF-8

Check malformed and truncated sequences, overlong forms, surrogate values,
values above U+10FFFF, BOM state, repeated leading U+FEFF, ASCII input, and a
multibyte character split across buffers.

### UTF-16

Check LE and BE, BOM and BOM-less input, odd lengths, surrogate pairs, lone
surrogates, short input, and line separators at buffer boundaries.

Preserve the conservative BOM-less UTF-16 rule: normalize only when strict
decoding in the opposite byte order is impossible. If both orders decode,
refuse without modifying bytes. Do not replace this with a confidence guess.

### UTF-32

Check LE and BE, BOM and BOM-less input, invalid scalars, surrogate-range
values, lengths not divisible by four, truncated units, and NUL-heavy binary
that resembles UTF-32. Do not copy UTF-16's full-file opposite-order rule into
UTF-32 without proving LEN needs it.

### BOMs and legacy encodings

Distinguish an encoding signature from decoded U+FEFF. Check no BOM, the right
BOM, repeated BOMs, BOM-only files, embedded U+FEFF, and conflicting evidence.

LEN's legacy path must not decode and re-encode text. Verify that accepted
single-byte encodings map CR/LF to their ASCII bytes and that accepted multibyte
encodings have explicit evidence that raw CR/LF replacement cannot damage a
character. Challenge undefined mappings, truncated tails, stateful codecs,
aliases, and platform-specific behavior.

## 5. Detection and binary classification

Challenge short files, ASCII, BOM-less UTF-16/32, non-Latin and private-use
text, NUL-heavy input, high-entropy binary, and legacy bytes that resemble
Unicode.

The current detector checks entropy before BOM and Unicode patterns. Treat
reordering as a coordinated policy change, not cleanup. Do not modify
`UnicodeDetector.cs` or `TextValidation.cs` without explicit approval and
cross-repository regression evidence.

False refusal is safer than corruption but remains a correctness finding when
supported input is rejected contrary to the documented policy.

## 6. Write, backup, and recovery

Trace source/destination identity, concurrent source changes, temporary-file
naming and exclusion, flushing, output verification, BOM verification,
metadata preservation, read-only files, existing backups, backup SHA-256
binding, linked paths, cancellation boundaries, replacement failure, and
cleanup.

Verify that replacement preserves file attributes and creation, last-write,
and last-access timestamps. Check the actual implementation order: temporary
output is verified, the destination is revalidated, metadata is applied to the
temporary file, an optional backup is created and hash-verified, and only then
is the source replaced.

A failed conversion must leave the source no less recoverable. A leftover
temporary file or backup may be acceptable, but reports and docs must say so.
LEN creates verified backups; it does not provide verified restore.

## 7. False passes and false failures

Search for false success where permissive decoding, sampled detection, partial
output, weak verification, lost worker errors, cancellation, report failures,
preview labels, or missing coverage accounting produce a clean result.

Search for false failure where valid boundary cases, BOM handling, aliases,
empty files, safe refusal, environment limitations, or test-harness faults are
misreported as LEN defects.

Trace suspicious paths to the final console row, report row, summary, and exit
code.

## 8. CLI and diagnostics

Check `Program.cs` against `docs/CLI.md` for normal conversion, `-WhatIf`,
`-ValidateOnly`, `-DetectOnly`, `-Backup`, `-Report`, `-Quiet`, `-Verbose`,
`-FailOnChanges`, `-Deterministic`, `-MaxParallelism`, `-BasePath`, `-Include`,
`-Exclude`, `-FullPath`, `--help`, and `--version`.

Test wildcard safety, file-name versus relative-path matching, empty filters,
default directory exclusions, relative roots, linked roots, report-file
self-exclusion, and version consistency between project, manifest, built
program, help, and release tag.

Keep invalid arguments, changes needed, failures, cancellation, safe refusal,
and linked-root rejection distinct. `ReasonCode` values are a stable contract;
`Diagnostic` should explain the particular failure. Check CSV escaping,
ordering, report-write failure, filenames containing delimiters, and exactly
one terminal row per processed file.

## 9. Concurrency

Inspect shared counters, collections, console output, report assembly,
cancellation, exception aggregation, and ordering. Single-threaded and parallel
runs must make the same per-file decisions, and worker failures must remain in
the summary and exit code.

The tests disable xUnit class parallelism because end-to-end tests redirect
process-wide console streams. Do not remove that guard without eliminating the
shared-state race.

## 10. Tests

Judge behavioral risk, not coverage percentage. Look for tests that reproduce
the implementation, use permissive expected decoding, assert labels without
bytes, miss their named branch, lose concurrent callbacks, swallow exceptions,
depend on order or timing, or run stale binaries.

High-value coverage includes malformed Unicode, BOM states, BOM-less UTF-16,
split characters and separators, mixed endings, legacy byte preservation,
no-op files, backup mismatch, replacement failure, linked paths, source
mutation, cancellation, parallel equivalence, report reconciliation, and exit
precedence.

Every confirmed defect needs a regression test that fails before the fix and
checks the externally meaningful result. For refusal, preview, cancellation,
and error cases, assert original source bytes too.

## 11. Shared detector boundary

LEN's local parity workflow compares `UnicodeDetector.cs` with EncodingChecker.
It does not compare `TextValidation.cs` and does not include CorpusTesters.
`TextEncoding.cs` is an application wrapper and may differ.

For an approved shared-detector or `UTF.Unknown` change, inspect all three
repositories and explicitly compare both `UnicodeDetector.cs` and
`TextValidation.cs`, even though LEN's local workflow enforces only the first
comparison against EC. Compare paired before/after results, run parity and the
relevant corpus audit, reconcile every input to one outcome, and separately
flag material distribution shifts. Completeness does not prove classification
correctness. Never modify source corpora or sibling repositories without
authorization.

## 12. Release pipeline

Review `.github/workflows/ci.yml`, `detector-parity.yml`, and `release.yml` as
part of release readiness. Check workflow permissions, signing-secret scope,
tag values used in commands or artifact names, mutable third-party actions,
and whether untrusted repository content runs before signing or publication.

Verify both published packages: the framework-dependent build and the
self-contained Windows executable. Confirm the release tag matches project and
manifest versions, signing behavior is stated truthfully, and checks and hashes
refer to the exact artifacts being released.

## 13. Evidence standard

Every substantive finding must include:

**Location** — file and symbol.  
**Severity** — Critical / High / Medium / Low.  
**Confidence** — Confirmed / High / Unconfirmed.  
**Problem** — what is wrong.  
**Trigger** — concrete condition.  
**Impact** — what LEN or the user experiences.  
**Evidence** — code path, failing test, runtime behavior, exact bytes, or
authoritative platform behavior.  
**Recommended fix** — smallest root-cause correction.  
**Regression test** — specific observable result that must be asserted.

Do not call a plausible race or platform concern confirmed without sufficient
evidence. Run a small disposable experiment when behavior is uncertain.

## Severity

**Critical:** credible irreversible loss or widespread silent corruption.

**High:** false success after incorrect normalization; silent encoding, BOM,
text, or unrelated-byte changes; major backup/replacement failure; lost worker
failures affecting many files.

**Medium:** meaningful false detection/refusal; preview/write disagreement;
misleading result, report, diagnostic, or exit code; significant test gap;
concurrency inconsistency without demonstrated corruption.

**Low:** narrow diagnostic or documentation problem, credible maintenance
hazard, or non-critical performance issue.

Do not inflate severity for rare, local, or unreachable paths.

## Supporting skills

Use focused skills when they add evidence: `code-review` for bounded diffs,
`diagnosing-bugs` for reproduction, `test-anti-patterns` for independent test
audit, `modern-csharp-coding-standards` for material C# issues,
`type-design-performance` for demonstrated performance risks, and `csharp-docs`
for documentation claims. Verify their output against LEN's actual bytes and
pipeline.

## Independent review

For a from-scratch review, inspect the current repository directly. Treat old
reports and fixes as hypotheses, do not assume green tests prove correctness,
and use historical defects afterward as regression targets.

## Final report

Start with an executive verdict, release readiness, severity counts, and the
largest remaining risk. Use **READY**, **READY WITH MINOR FOLLOW-UP**,
**NOT READY**, or **INSUFFICIENT EVIDENCE**.

List confirmed findings by severity as `LEN-REV-XXX` using every evidence field
above. Keep unconfirmed concerns separate and state what would settle them.
Then list meaningful test gaps, **Must fix / Should fix / Can defer**, and the
residual risks the review could not establish.

## Completion rule

Do not call a full LEN review complete unless it addresses detection and
full-file validation; UTF-8/16/32; BOM and BOM-less behavior; legacy byte
safety; all supported line endings; buffer boundaries; preview and validation;
temporary output and verification; backup and replacement; linked paths and
concurrent changes; exceptions and diagnostics; reports and exit codes;
concurrency and cancellation; regression tests; documentation; and release
readiness.

If access or evidence prevents completion, name the unreviewed areas.
Correctness, byte preservation, and diagnostic honesty take priority over
style, modernization, and passing existing tests.
