# LineEndingNormalizer

LineEndingNormalizer (LEN) is a Windows command-line tool that changes text
line endings to CRLF, LF, or CR without changing the file's encoding.

It preserves the original byte-order mark (BOM), file attributes, and
timestamps. Files that cannot be handled safely are left unchanged.

To change character encodings, use
[EncodingChecker](https://github.com/amrali-eg/EncodingChecker).

## Safe workflow

1. Preview the operation with `-WhatIf`.
2. Review files that would change or be refused.
3. Run the same command without `-WhatIf`.
4. Add `-Backup` when you want an independently hash-verified `.bak` copy.

```powershell
LineEndingNormalizer.exe -BasePath C:\Source -Include "*.cs,*.txt" -Target LF -WhatIf
LineEndingNormalizer.exe -BasePath C:\Source -Include "*.cs,*.txt" -Target LF -Backup
```

## What LEN does

- Detects ASCII, UTF-8, UTF-16, UTF-32, and a conservative set of legacy
  encodings.
- Normalizes CRLF, LF, and CR. Unicode NEL, LS, and PS are also normalized
  in Unicode files.
- Streams large files instead of loading them fully into memory.
- Strictly decodes and re-encodes Unicode; invalid input is refused.
- Copies legacy text byte-for-byte except for CR and LF bytes, and only when
  that operation has been verified safe for the detected encoding.
- Writes to a temporary file, verifies the result, and installs it only after
  all checks pass.
- Skips linked files and linked directories in every mode.

## Important safety limit

Without a byte-order mark, UTF-16 bytes usually read as valid text in *both*
byte orders, so the file itself cannot say which one is right. LEN refuses to
normalize BOM-less UTF-16 unless the bytes prove the byte order -- which for
ordinary Latin text they do not, because byte-swapped Latin characters land in
a valid CJK range. **Expect most BOM-less UTF-16 files to be refused.**

Conversion proceeds only when the opposite byte order is structurally
impossible. Adding a correct BOM, or converting the encoding with an explicitly
chosen source codec, resolves the ambiguity.

See [Safety and recovery](docs/SAFETY.md) for the complete safety model and
known limits.

## Common commands

Preview conversion:

```powershell
LineEndingNormalizer.exe -BasePath . -Include "*" -Target CRLF -WhatIf
```

Validate in CI without writing:

```powershell
LineEndingNormalizer.exe -BasePath . -Include "*.cs" -Target CRLF -ValidateOnly -FailOnChanges -Quiet
```

Detect encoding and line endings only:

```powershell
LineEndingNormalizer.exe -BasePath . -Include "*" -DetectOnly > detection.csv
```

Write a detailed CSV report:

```powershell
LineEndingNormalizer.exe -BasePath . -Include "*" -Target LF -WhatIf -Report report.csv
```

Run `LineEndingNormalizer.exe --help` for the complete switch reference and
examples, or read [Command-line reference](docs/CLI.md).

## Documentation

- [Command-line reference](docs/CLI.md)
- [Safety and recovery](docs/SAFETY.md)
- [Encoding detection and supported text](docs/DETECTION.md)
- [Independent audit](docs/SAFETY-AUDIT.md)
- [Release checklist](docs/RELEASE-CHECKLIST.md)

## Reports

`-Report` writes UTF-8 CSV with these fields:

```text
File,Encoding,BOM,LineEnding,Target,Result,ReasonCode,Diagnostic
```

`ReasonCode` is stable for scripts. `Diagnostic` explains the particular
failure for people. The report always describes the original file's detected
state, even when normalization succeeds.

## Requirements and downloads

LEN targets Windows and .NET 10. Download the framework-dependent or
self-contained build from the
[latest release](https://github.com/amrali-eg/LineEndingNormalizer/releases/latest).

To build locally:

```powershell
dotnet build LineEndingNormalizer.slnx --configuration Release
dotnet test LineEndingNormalizer.slnx --configuration Release
```

## Related projects

- [EncodingChecker](https://github.com/amrali-eg/EncodingChecker) converts
  text encodings through a strict, verified pipeline.
- [CorpusTesters](https://github.com/amrali-eg/CorpusTesters) audits the shared
  detector and conversion behavior against public corpora.

## License

[MPL 2.0](LICENSE). Third-party notices are in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
