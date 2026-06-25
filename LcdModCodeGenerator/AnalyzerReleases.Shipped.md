; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 0.1

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|--------------------
LcdMOD001 | LcdModCodeGenerator | Warning | App implements interface containing the same control more than once

## Release 0.2

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|--------------------
LcdMOD002 | LcdModCodeGenerator | Warning | Duplicate screen config Id detected across IScreenConfig implementations
LcdMOD003 | LcdModCodeGenerator | Warning | Surface script should use generated component properties instead of raw Config access
