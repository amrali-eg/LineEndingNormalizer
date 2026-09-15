# Safety and recovery

LEN changes line endings only. It never intentionally changes a file's
character encoding.

## Conversion sequence

For each file that needs normalization, LEN:

1. Opens and scans the source.
2. Strictly validates the detected encoding.
3. Refuses ambiguous BOM-less UTF-16.
4. Writes normalized content to a temporary file beside the source.
5. Verifies the temporary file's content and BOM policy.
6. If requested, copies the original to `.bak`, flushes it, and verifies its
   SHA-256 against the exact source bytes used during conversion.
7. Rechecks the destination's identity and metadata.
8. Applies preserved attributes and timestamps to the temporary file.
9. Installs the temporary file atomically on Windows where supported.

If any required step fails, LEN does not install the temporary output.

## Unicode files

Unicode input is strictly decoded, normalized as Unicode text, strictly
re-encoded in the same encoding, then independently decoded and verified.
Replacement fallback is not allowed.

The original BOM state is preserved. If a file begins with repeated BOM code
points, only the first encoding signature is treated as a BOM; additional
leading U+FEFF characters remain part of the text.

### BOM-less UTF-16

Without a BOM, UTF-16 bytes are usually valid as both UTF-16LE and UTF-16BE:
byte-swapped Latin text lands in a valid CJK range, so ordinary ASCII content
decodes cleanly either way. Choosing the wrong order can change which characters are treated as line
separators. LEN therefore attempts a strict full-file decode in the opposite
byte order before conversion. If both byte orders are valid, LEN reports
`AmbiguousBomlessUtf16` and leaves the file unchanged.

This is intentionally conservative, and the practical effect is broad: LEN
converts BOM-less UTF-16 only when the opposite byte order is structurally
impossible, so **most BOM-less UTF-16 files are refused**. Two similar-looking
files can behave differently, because whether the opposite order fails depends
on the characters the file happens to contain.

Establishing this costs a second complete read of the file. That is deliberate:
a decision to rewrite every line ending in a file should not rest on a sample.

Add the correct BOM or use an encoding conversion tool with an explicitly
selected source encoding before retrying.

### BOM-less UTF-32

The same opposite-byte-order check applies to BOM-less UTF-32: if both
UTF-32LE and UTF-32BE strictly decode the whole file, LEN reports
`AmbiguousBomlessUtf32` and leaves the file unchanged.

In practice this refusal is rare to the point of being close to
theoretical. Every line separator LEN recognizes (CR, LF, NEL, LS, PS) is a
small scalar value, and a 4-byte UTF-32 group that encodes one of them
always pushes the *opposite* byte order's value for that same group outside
the valid `U+0000`-`U+10FFFF` range. A file therefore cannot both need line
ending conversion and be genuinely ambiguous between the two byte orders at
the same time - if it needs conversion, the opposite order already fails to
decode, and this guard has nothing to add. It still exists so a BOM-less
UTF-32 file that manages to be ambiguous is never rewritten under an
unproven byte order, matching the same conservative rule as UTF-16.

## Legacy files

LEN does not decode and re-encode legacy text. It copies all bytes unchanged
except CR (`0x0D`) and LF (`0x0A`). A legacy encoding is accepted only when
those byte values have been verified safe to treat as line separators.

## Backups

`-Backup` creates `<file>.bak` before the converted file is installed. The
backup is flushed to disk and SHA-256 verified against the bytes that LEN
actually read for conversion. A mismatch produces `BackupVerificationFailed`
and aborts installation.

A `.bak` file is a recoverable byte copy, not a built-in restore system. LEN
does not currently provide a restore command. Restore by verifying and copying
the backup with normal filesystem tools.

## Links and concurrent changes

LEN rejects a linked/reparse-point root and skips linked files and directories
in normalization, preview, validation, and detection modes.

Immediately before installation, LEN checks that the destination still has the
expected existence, size, timestamp, and non-link status. This greatly reduces
accidental overwrite risk, but it cannot eliminate the narrow race in which a
different process modifies a path between the final check and replacement.

## Temporary files and installation

The source is never edited in place. LEN writes a sibling temporary file and
verifies it first. On Windows, installation uses `ReplaceFile`; the portable
fallback is used only if that API is unavailable, not after a real replacement
failure.

An interrupted process can leave a temporary file or backup, but it should not
leave a partially written source file. LEN excludes its own temporary and
backup files from later scans.
