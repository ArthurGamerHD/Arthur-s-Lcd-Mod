; Unshipped analyzer release
; https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules
Rule ID | Category | Severity | Notes
--------|----------|----------|-------
LcdMOD004 | LcdModCodeGenerator | Warning | Client and server code must not import each other
LcdMOD005 | LcdModCodeGenerator | Warning | Interactive app or surface config must inherit ScreenConfigInteractive
