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
