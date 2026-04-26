# AI Translation Instruction

When creating or updating localization text (`LCDMod_*` keys), always update **all supported locale files** in:

- `Graph/Content/Data/Localization/MyTexts.resx`
- `Graph/Content/Data/Localization/MyTexts.<locale>.resx` (every locale present in this folder)

Rules:

1. Do not add keys to only one or two languages.
2. Keep key names identical across all locale files.
3. Translate in each language's **native idiom** (natural phrasing for native speakers), not literal word-by-word translation.
4. Preserve placeholders and format tokens exactly (`{0}`, `{1}`, `%`, units, punctuation needed by format strings).
5. If a high-quality translation is uncertain, use the English fallback text in that locale file and mark it with `TODO_TRANSLATION` in a comment next to the value.
6. Before finishing, verify every new key exists in every `MyTexts*.resx` file.

