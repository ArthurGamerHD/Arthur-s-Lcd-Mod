; Unshipped analyzer release
; https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules
Rule ID | Category | Severity | Notes
--------|----------|----------|-------
LcdMOD004 | LcdModCodeGenerator | Warning | Client and server code must not import each other
LCDCFG001 | LcdModCodeGenerator | Error | App IDs must be stable positive values
LCDCFG002 | LcdModCodeGenerator | Error | App IDs must be unique
LCDCFG003 | LcdModCodeGenerator | Error | Generated app names must be unique
LCDCFG004 | LcdModCodeGenerator | Error | App metadata targets must be supported concrete partial app classes
LCDCFG005 | LcdModCodeGenerator | Error | Generated app names must be valid identifiers
LCDCFG010 | LcdModCodeGenerator | Error | Component metadata targets must be supported partial app classes
LCDCFG011 | LcdModCodeGenerator | Error | Component slots must be non-empty constants
LCDCFG012 | LcdModCodeGenerator | Error | Component types must be constructible ConfigComponent subclasses
LCDCFG013 | LcdModCodeGenerator | Error | Aggregated app schemas must not contain duplicate slots
LCDCFG014 | LcdModCodeGenerator | Error | Generated component property names must be unique
LCDCFG015 | LcdModCodeGenerator | Error | Generated component properties must not collide with handwritten members
LCDCFG016 | LcdModCodeGenerator | Error | Repeated component types require explicit property names
LCDCFG017 | LcdModCodeGenerator | Error | Semantic block references require explicit semantic slots and property names
LCDCFG020 | LcdModCodeGenerator | Error | Surface metadata targets must be supported partial surface classes
LCDCFG021 | LcdModCodeGenerator | Error | Surfaces must reference a concrete registered app
LCDCFG022 | LcdModCodeGenerator | Error | Surface construction must agree with its declared app
