# Independent safety audit

The detector shared by LEN and
[EncodingChecker](https://github.com/amrali-eg/EncodingChecker) is tested by
[CorpusTesters](https://github.com/amrali-eg/CorpusTesters) against four public
corpus families. Corpus metadata—not filenames or detector output—supplies the
reference encoding.

The audit compares exact Unicode scalar sequences without normalization and
uses strict decoder and encoder fallbacks. It also contains negative controls,
codec-conformance probes, detector-parity checks, and run-to-run distribution
alarms.

## How the results apply to LEN

Detection results apply to LEN because the detector source is kept in parity
across the repositories. EncodingChecker's end-to-end conversion percentages
do not directly measure LEN's writer: LEN preserves the source encoding and
uses a different, narrower line-ending-only operation.

For Unicode, LEN strictly decodes and re-encodes the same codec. For accepted
legacy text, LEN does not decode characters at all; it rewrites verified CR/LF
bytes and copies every other byte unchanged.

## Important limitations

- A corpus can measure only the files and invalid sequences it contains.
- Legacy codec mappings can differ between Python, .NET, ICU, iconv, and
  vendor profiles. A disagreement does not by itself prove which mapping is
  universally correct.
- Unsupported files are unmeasured, not passes or failures.
- Detector accuracy and conversion fidelity are separate questions.
- A shared mistake in corpus metadata or a reference decoder can still affect
  an audit result; independent controls reduce but do not remove that risk.

The public per-file evidence and methodology are maintained in CorpusTesters.
Safety claims should cite a clean, committed build and its recorded hashes,
not only a version name.

## Release records

### v1.5.0 — not corpus-audited

**No corpus run has been performed against LEN.** The audit described above
measures the shared detector through EncodingChecker; nothing has measured
LEN's writer against a corpus of real files. Detection results carry over
because the detector source is in parity, verified below. Everything specific
to this release — the BOM-less UTF-16 refusal, backup hash verification, the
refusal reporting contract — rests on the regression suite, not on measurement.

```
commit    ac9035cc2a43bd50c338bf2e3d4280d085ab1c08   (annotated tag v1.5.0)
project   1.5.0     manifest 1.5.0.0     binary reports 1.5.0
tests     295 passed, 0 failed
build     0 warnings under -warnaserror
```

Published artifacts, digests as recorded by GitHub:

```
LineEndingNormalizer-1.5.0-framework-dependent.zip
  sha256:0e206e0db83c928984f229d5cac9cf0119c3424ff4bfaad64a1dc13970d672b1
LineEndingNormalizer-1.5.0-win-x64-self-contained.zip
  sha256:51c2f7205920e26d7ca02b4756e7de71d658bc70304f4eea9fc4e75050b223b6
```

These are the archives, not the assembly inside them. The release workflow
refuses to publish unless the git tag, the project version, and the application
manifest agree.

Detector parity at release, over three clean checkouts level with their
remotes — `TextValidation.cs`, `UnicodeDetector.cs`, and `TextEncoding.Strict`
identical across all three:

```
EncodingChecker        f1b6c09
LineEndingNormalizer   ac9035c   (this release)
CorpusTesters          3830eff
```

Parity proves the three copies agree, not that they are correct. Three
identical copies of a wrong detector would pass it.

**What is still unmeasured for this release.** Filesystem and replacement
behaviour on FAT32, exFAT, and network shares; cancellation at each backup
stage; reconciliation of report rows under high file counts; and a smoke check
of the published archives rather than a locally built binary. These are
untested rather than known-good.

### v1.6.0 — not corpus-audited

**No corpus run has been performed against LEN.** As with v1.5.0, detection
results carry over through detector parity with EncodingChecker, verified
below. This release adds one new refusal path (BOM-less UTF-32 ambiguity,
mirroring the existing UTF-16 guard) and closes a test-coverage gap in the
backup hash-verification check; both rest on the regression suite, not on
corpus measurement.

**Correction (added in v1.7.0):** the UTF-32 guard this version ships is
insufficient — a BOM-less UTF-16 file can misdetect as UTF-32 and pass the
opposite-order test unchanged, so v1.6.0 as published can silently corrupt
such a file. v1.7.0 replaces it with an unconditional refusal. See that
version's entry below and [RELEASE-NOTES-v1.7.0.md](RELEASE-NOTES-v1.7.0.md).

```
commit    31dd78ac4c28a9e8713c09c49a13d2f7f8733add   (annotated tag v1.6.0)
project   1.6.0     manifest 1.6.0.0     binary reports 1.6.0
tests     299 passed, 0 failed
build     0 warnings
```

Published archives, digests verified against a local download rather than
trusting GitHub's own report of them — and confirmed to equal what GitHub
itself reports as each asset's digest:

```
LineEndingNormalizer-1.6.0-framework-dependent.zip
  sha256:5e84eccbc1e2d28ae09c9b25a775d43d20417756f231caf783019d6469215baf
LineEndingNormalizer-1.6.0-win-x64-self-contained.zip
  sha256:b3c174f05a779290e6db691b283bd5b29c3a1fceb5b535f3fb5b27181f63fb1c
```

**The executables reproduce byte-for-byte once the checkout path matches.**
An initial rebuild from a clean checkout of this same commit, on the same SDK
(10.0.401) and runtime (10.0.12) the release workflow used, produced files of
identical size but not identical bytes: exactly 160 bytes differed in each
executable, regardless of the executable's total size. Diffing the two
executables byte-by-byte traced every difference to one location: the
compiler-generated support class for `FilePatternMatcher.cs`'s
`[GeneratedRegex]` attribute embeds a hash of the source file's absolute
path in its generated type name, so building from a different checkout
directory than the release workflow used changes those bytes without
changing behavior. Rebuilding from `D:\a\LineEndingNormalizer\LineEndingNormalizer`
— the default GitHub-hosted Windows runner workspace path — reproduced both
published executables exactly:

```
framework-dependent  published  ae9d26c5b31a0090a9769d736f9d855376019278c09ace4f0852bc1990ad3caa
                     rebuilt    ae9d26c5b31a0090a9769d736f9d855376019278c09ace4f0852bc1990ad3caa
self-contained       published  057c8ce19436f999f16789fb4adc2203281bb2ee525ac1cb0ab403bd86c3a0db
                     rebuilt    057c8ce19436f999f16789fb4adc2203281bb2ee525ac1cb0ab403bd86c3a0db
```

Reproducing this build therefore requires checking out to that exact path
(or wherever the release workflow itself runs), not merely the same source,
SDK, and runtime — a limitation specific to `[GeneratedRegex]`'s naming
scheme, not to single-file publishing.

These are the archives, not the assembly inside them. The release workflow
refuses to publish unless the git tag, the project version, and the application
manifest agree.

Detector parity at release, over three clean checkouts level with their
remotes — `TextValidation.cs`, `UnicodeDetector.cs`, and `TextEncoding.Strict`
identical across all three:

```
EncodingChecker        0002c44
LineEndingNormalizer   31dd78a   (this release)
CorpusTesters          d84158f
```

Parity proves the three copies agree, not that they are correct. Three
identical copies of a wrong detector would pass it.

**What is still unmeasured for this release.** Everything listed under
v1.5.0's "still unmeasured" remains true, plus: no filesystem-level exercise
of the new UTF-32 guard (its regression tests call the internal guard
directly, since a file cannot both need conversion and be genuinely
byte-order-ambiguous under UTF-32 — see [SAFETY.md](SAFETY.md) and
[RELEASE-NOTES-v1.6.0.md](RELEASE-NOTES-v1.6.0.md) for why).

### v1.7.0 — not corpus-audited

**No corpus run has been performed against LEN.** As with v1.5.0 and v1.6.0,
detection results carry over through detector parity with EncodingChecker,
verified below. This release fixes the v1.6.0 UTF-32 gap recorded above,
adds a preflight check and atomic write for `-Report`, and adds two
directory-coverage counters to the run summary; all rest on the regression
suite, not on corpus measurement.

```
commit    8c31fed98281d70a13c6bf22f58b28d19e9beeaf   (tag v1.7.0)
project   1.7.0     manifest 1.7.0.0     binary reports 1.7.0
tests     316 passed, 0 failed
build     0 warnings
```

Published archives, digests verified against a local download rather than
trusting GitHub's own report of them — and confirmed to equal what GitHub
itself reports as each asset's digest:

```
LineEndingNormalizer-1.7.0-framework-dependent.zip
  sha256:dc01dd0c07516454b0e6d4f7780358287e5c82cc526b464463e4efc0d7785e74
LineEndingNormalizer-1.7.0-win-x64-self-contained.zip
  sha256:628f51a014b4416511109efe3935b7dc5bbae15e3674ee8ada136e3e90824d7e
```

**The executables reproduce byte-for-byte, checked directly this time.**
Learning applied from v1.6.0's investigation: rebuilt directly from a clean
checkout at `D:\a\LineEndingNormalizer\LineEndingNormalizer` (the GitHub-hosted
Windows runner workspace path `[GeneratedRegex]`'s path-derived naming
requires), on the same SDK (10.0.401) and runtime (10.0.12) the release
workflow used. Both executables matched the published archives' contents
exactly on the first attempt:

```
framework-dependent  published  8c85ae0ac4a01b81d2ea9a80c9cf0fe67ab3cec3a5ad3615d32c140a25498e70
                     rebuilt    8c85ae0ac4a01b81d2ea9a80c9cf0fe67ab3cec3a5ad3615d32c140a25498e70
self-contained       published  00f74319f484a8db6304e673d070dc82c3bd88362a3f3adeef1334573483f5cf
                     rebuilt    00f74319f484a8db6304e673d070dc82c3bd88362a3f3adeef1334573483f5cf
```

These are the archives, not the assembly inside them. The release workflow
refuses to publish unless the git tag, the project version, and the application
manifest agree.

Detector parity at release, over three clean checkouts level with their
remotes — `TextValidation.cs`, `UnicodeDetector.cs`, and `TextEncoding.Strict`
identical across all three:

```
EncodingChecker        0002c44
LineEndingNormalizer   8c31fed   (this release)
CorpusTesters          d84158f
```

Parity proves the three copies agree, not that they are correct. Three
identical copies of a wrong detector would pass it.

**What is still unmeasured for this release.** Everything listed under
v1.6.0's "still unmeasured" remains true. The new `-Report` preflight and
atomic-write paths, and the two new directory-coverage counters, are
end-to-end tested (real files, real CLI invocation via `Program.Main`) rather
than only unit-level, and each was mutation-tested — but none of that is
corpus measurement either.
