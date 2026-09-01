# Encoding detection

LEN must identify enough about a file to normalize line endings without
changing its encoding. Detection is deliberately conservative: an unknown or
unsafe file is skipped.

## Detection order

1. Read at most 64 KiB from the start of the file.
2. Reject sufficiently large, high-entropy samples as likely binary.
3. Check Unicode byte patterns for ASCII, UTF-8, UTF-16LE/BE, and
   UTF-32LE/BE, with or without a BOM.
4. If Unicode is not identified, ask UTF.Unknown for a legacy candidate.
5. Strictly validate the candidate and its text quality.
6. For normalization, require the encoding to be safe for LEN's raw CR/LF
   byte transformation.

The explicit exclusion from this release is important: LEN does not bypass
the entropy filter merely because bytes at the start resemble a Unicode BOM.
That detector-order change requires coordinated corpus testing across LEN,
EncodingChecker, and CorpusTesters before it can be considered.

## Supported text

Unicode handling covers:

- ASCII
- UTF-8
- UTF-16LE and UTF-16BE
- UTF-32LE and UTF-32BE

BOM-less UTF-16 that is valid in both byte orders is detected but refused for
conversion; see [Safety and recovery](SAFETY.md#bom-less-utf-16).

For legacy encodings, every single-byte code page must map CR and LF to their
ASCII byte values. Multi-byte encodings must be in the verified allowlist in
`TextEncoding.cs`. The source code is authoritative because the list can
change as implementations are tested.

## What a detection label means

Legacy encoding detection is not proof of the file author's original codec.
Several encodings can accept the same bytes, especially for ASCII-only text.
LEN's safety comes from limiting its operation: for accepted legacy files it
does not reinterpret characters, and changes only verified CR/LF bytes.

`-DetectOnly` reports the detector's best result; it does not convert files.
Linked files, linked directories, backups, temporary files, and excluded paths
are not scanned.
