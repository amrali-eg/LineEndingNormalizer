# Command-line reference

## Basic form

```text
LineEndingNormalizer.exe -BasePath <directory> [options]
```

Normalization is the default mode. The default target is CRLF and the default
include pattern is `*`.

## File selection

| Option | Meaning |
|---|---|
| `-BasePath <directory>` | Root directory to scan. Required. |
| `-Include "<patterns>"` | Comma-separated wildcard patterns to include. |
| `-Exclude "<patterns>"` | Comma-separated wildcard patterns to exclude after inclusion. |
| `-FullPath` | Show absolute paths instead of paths relative to the root. |

An explicitly supplied `-Include` or `-Exclude` must contain at least one
non-empty pattern. Values such as `""`, `",,,"`, or whitespace are rejected.

A pattern without `/` or `\` matches filenames at any depth. A pattern with a
separator matches the path relative to `-BasePath`. `/` and `\` are equivalent.
`*` and `?` are the supported wildcards; `*` may cross directory separators.

LEN always excludes `.git`, `.svn`, `.hg`, `.vs`, `.idea`, `bin`, `obj`,
`node_modules`, `packages`, `dist`, `build`, and `target`. It also excludes
`.bak`, its own temporary files, and the current report path. Linked files and
linked directories are skipped in every mode.

## Target and modes

| Option | Meaning |
|---|---|
| `-Target <CRLF|LF|CR>` | Desired line endings. `WINDOWS`, `UNIX`, and `MAC` are aliases. |
| `-WhatIf` | Preview normalization without writing. |
| `-ValidateOnly` | Report files that do not match the target without writing. |
| `-DetectOnly` | Write detection CSV rows to standard output; no target is used. |
| `-Backup` | Before installation, create and hash-verify `<file>.bak`. |
| `-FailOnChanges` | Return exit code 2 if any file requires conversion. |

Invalid combinations are rejected instead of silently ignored:

- `-Verbose` with `-Quiet`
- `-DetectOnly` with `-ValidateOnly`, `-Target`, `-WhatIf`, `-Backup`,
  `-FailOnChanges`, `-Quiet`, or `-Verbose`
- `-ValidateOnly` with `-WhatIf` or `-Backup`
- `-WhatIf` with `-Backup`

## Output and performance

| Option | Meaning |
|---|---|
| `-Report <path>` | Write a UTF-8 CSV report in any mode. |
| `-Verbose` | Include unchanged files in console output. |
| `-Quiet` | Suppress per-file console output; summaries and errors remain. |
| `-Deterministic` | Process and print files in ordinal path order. Reports are always sorted. |
| `-MaxParallelism <N>` | Maximum concurrent files. Default: the smaller of processor count and 4. |

Normalization reports contain:

```text
File,Encoding,BOM,LineEnding,Target,Result,ReasonCode,Diagnostic
```

## Examples

Convert source files to LF:

```powershell
LineEndingNormalizer.exe -BasePath C:\Source -Include "*.cs,*.txt" -Target LF
```

Preview a whole tree:

```powershell
LineEndingNormalizer.exe -BasePath . -Include "*" -Target CRLF -WhatIf
```

Exclude generated source:

```powershell
LineEndingNormalizer.exe -BasePath . -Include "*.cs" -Exclude "*.g.cs,*.designer.cs" -Target CRLF
```

Validate in CI:

```powershell
LineEndingNormalizer.exe -BasePath . -Include "*" -Target LF -ValidateOnly -FailOnChanges -Quiet
```

Normalize with verified backups and a report:

```powershell
LineEndingNormalizer.exe -BasePath . -Include "*" -Target LF -Backup -Report report.csv -Deterministic
```

## Exit codes

| Code | Meaning |
|---:|---|
| 0 | Clean run. |
| 1 | Invalid arguments. |
| 2 | `-FailOnChanges` found files requiring conversion. |
| 3 | One or more files failed, or the report could not be written. |
| 4 | Cancelled with Ctrl+C. |
| 5 | Base directory not found. |
| 6 | Base directory is a link, junction, or other reparse point. |

Codes 0–4 match EncodingChecker's general exit-code meanings.
