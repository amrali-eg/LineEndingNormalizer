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
