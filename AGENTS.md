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


# MVVM / Control Rendering Instruction

When a control is backed by a view model, do not recreate that view model every frame.

Rules:

1. Keep view models stable across render/update frames and mutate their value fields when data changes.
2. Recreate or fully refresh view models only when parent layout changes, such as screen size, parent control bounds, localization/layout cache invalidation, or a different control composition.
3. Put layout-sensitive cached text, measured values, icon choices, and style values behind explicit layout invalidation instead of rebuilding them in normal per-frame updates.
4. Lists and grids should reuse existing item view models keyed by stable model identity whenever possible.
