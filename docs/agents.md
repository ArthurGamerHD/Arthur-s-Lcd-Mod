# AI Translation Instruction

When creating or updating localization text (`LcdMod_*` keys), always update **all supported locale files** in:

- `Graph/Content/Data/Localization/MyTexts.resx`
- `Graph/Content/Data/Localization/MyTexts.<locale>.resx` (every locale present in this folder)

Rules:

1. Do not add keys to only one or two languages.
2. Keep key names identical across all locale files.
3. Translate in each language's **native idiom** (natural phrasing for native speakers), not literal word-by-word translation.
4. Preserve placeholders and format tokens exactly (`{0}`, `{1}`, `%`, units, punctuation needed by format strings).
5. If a high-quality translation is uncertain, use the English fallback text in that locale file and mark it with `TODO_TRANSLATION` in a comment next to the value.
6. Before finishing, verify every new key exists in every `MyTexts*.resx` file.


# Rendering Instruction

When rendering LCD sprites or creating interactive hitboxes, always account for the anchor mismatch between text and rectangle/texture coordinates:

1. Text sprite positions are text anchors, not rectangle origins; text is visually centered around its anchor depending on `TextAlignment`.
2. Rectangle and texture-style layout math usually uses top/left rectangle origins.
3. Before adding a hitbox for rendered text, convert the measured text size and alignment into the actual on-screen rectangle.
4. Do not reuse a text sprite `Position` directly as a rectangle top-left corner.

# Icon Fallback Instruction

When resolving sprites or texture registrations for app data, never use `SquareSimple` as an icon fallback.

Rules:

1. Use the explicit `MissingIcon` sprite for missing, malformed, or unresolved icon data.
2. Reserve `SquareSimple` for geometric drawing primitives such as bars, panels, lines, and backgrounds.
3. If a specific fallback asset exists for a category, prefer that category fallback over `MissingIcon`.

# Space Engineers Source Instruction

Decompiled Space Engineers sources are available in `{binarypath}/../SE.Source`, where `{binarypath}` is read from the `[mdk]` `binarypath=` entry in `./Arthur-s-Lcd-Mod/mdk.local.ini`. For easy repo-local access, `./DumpSource.sh` creates or updates `./SE.Source` as a symlink to that folder when `./SE.Source` is absent or already a symlink.

If that `SE.Source` folder is missing or does not contain the needed source, run `./DumpSource.sh` only after ensuring `./Arthur-s-Lcd-Mod/mdk.local.ini` exists and has a valid `[mdk]` `binarypath=` pointing to the Space Engineers `Bin64` directory.


# MVVM / Control Rendering Instruction

When a control is backed by a view model, do not recreate that view model every frame.

Rules:

1. Keep view models stable across render/update frames and mutate their value fields when data changes.
2. Recreate or fully refresh view models only when parent layout changes, such as screen size, parent control bounds, localization/layout cache invalidation, or a different control composition.
3. Put layout-sensitive cached text, measured values, icon choices, and style values behind explicit layout invalidation instead of rebuilding them in normal per-frame updates.
4. Lists and grids should reuse existing item view models keyed by stable model identity whenever possible.

# IMyTextSurface

Space engineers uses Sandbox.ModAPI.Ingame.IMyTextSurface for TextSurfaceScript:

1. When creating any surface-related class or utility using IMyTextSurface, explicit import `using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;`
2. Do not let the default resolver find a match for `IMyTextSurface`
3. Do NOT import `Sandbox.ModAPI.Ingame` unless explicit necessary
4. Always prefer the `Sandbox.ModAPI` over `Sandbox.ModAPI.Ingame` when possible

# Acrux / Avalonia Compatibility Instruction

Acrux (`External/AcruxUI/Framework`) is intended to implement a **subset of Avalonia-compatible XAML**. Avalonia's XAML parser and source are the reference for supported syntax and behavior.

Rules:

1. Do not add features, syntax, properties, or functions to Acrux unless they are explicitly supported by Avalonia and, for XAML-facing features, accepted by Avalonia's XAML parser. Verify the corresponding parser support and control/API behavior in the Avalonia source before implementing an addition.
2. Prefer composing existing compatible controls, templates, styles, and bindings. Do not add convenience functions or app-specific shortcuts to Acrux merely to simplify a legacy-app migration. Existing Acrux-only behavior is not precedent for new incompatible features.
3. The only compatibility exceptions are explicitly identified extensions, such as `Markdown` controls or `LocalizedStrings`. Keep these separate from the core compatible API and expose them through explicit extension types/namespaces; they must not introduce incompatible core XAML syntax or change standard control semantics.
4. Resolve the Avalonia reference checkout from the repository-root `avalonia-source.local` file if it contains a valid path; otherwise check `<repo-root>/../Avalonia-main/`. Verify that the checkout contains the relevant source, including `src/Markup/Avalonia.Markup.Xaml` when checking parser support.
5. If no usable checkout is found, ask the user for the Avalonia source location before proceeding with work that depends on compatibility verification. Save the verified absolute checkout path as a single line in `<repo-root>/avalonia-source.local` and reuse it on later tasks. This machine-local file must remain untracked.
6. When the proper solution needs an Avalonia feature missing from Acrux, implement that compatible feature in Acrux instead of duplicating framework responsibilities in app code. Verify its API, XAML syntax, and behavior against the Avalonia source first, add focused coverage, and then use it in the app. Keep the implementation scoped to the required capability; this is not permission to invent Acrux-only shortcuts.

# Acrux UI Instruction

The LCD mod does not compile Acrux itself. Its UI runs inside the separate **Acrux UI** mod
(`External/AcruxUI`, see its README). This mod compiles the generated thin wrappers from
`$(AcruxUiToolkitRoot)/ClientApi/ClientApi.projitems`: same `Acrux.*` names, but every member forwards to the toolkit.

Rules:

1. Do not edit `ClientApi/` by hand; change Acrux (`External/AcruxUI/Framework`) or its generator
   (`Framework/Acrux.Generator/AcruxUiToolkitMirrorGenerator.cs`) and rebuild the toolkit, which regenerates ClientApi.
2. The client starts in `LcdModSessionComponent.LoadData` through `AcruxUiToolkitClient.Connect`; no UI type may be
   touched before that callback runs.
3. Base theme styles (`/Data/Gui/Styles/Acrux/...`) come from the toolkit; LCD-specific styles stay in this mod.
4. After changing the bridge, run `External/AcruxUI/Tests/run.sh` (two-assembly harness) before deploying.
5. Mod scripts may use only Space Engineers-whitelisted language elements and members (no `#pragma`, `GC`, reflection,
   ...), including generated code. After packing, run `External/AcruxUI/Tests/check-whitelist.sh`: it compiles the
   packed mods with the game's own compiler and whitelist. MDK's analyzer alone is not sufficient.

# Comments and Documentation Instruction

Applies to this repository and its submodules (`External/AcruxUI`, `External/AcruxUI/External/Adk`, ...).

Rules:

1. Do not write comments that explain what the code does; the reader can read the code.
2. Comment only when it adds something the code cannot say:
   - non-obvious game behavior or conventions (e.g. planet north is planet-local `-Y`, not `Up`);
   - the reason code looks unusual (a workaround, an ordering constraint, a whitelist limitation, a deliberate
     choice that would otherwise look like a bug).
3. XML `/// <summary>` docs:
   - API members (types/members exposed to other mods or clients, e.g. through the Acrux bridge/ClientApi, or
     public interfaces): always.
   - Other `public` members: optional.
   - Non-public members (`internal`, `private`, `protected` helpers): never; use a short `//` comment only if
     rule 2 applies.
4. Do not leave commented-out code or debug-only code in commits.
5. Comments use only plain text and characters typed on a regular keyboard: letters, digits, spaces and
   ``"\|<>,.:;?/-_+=*^~´`@#!$%&'()[]{}``. No other non-ASCII characters: no typographic dashes or quotes (`—`, `–`,
   `“”`, `’`), ellipsis (`…`), arrows (`→`), math or Greek symbols (`≈`, `×`, `°`, `π`), bullets or emoji. Write
   `-`, `"`, `'`, `...`, `->`, `~=`, `x`, `deg`, `pi` instead.
