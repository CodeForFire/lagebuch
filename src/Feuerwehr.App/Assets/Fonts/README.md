# Bundled fonts

All fonts below are licensed under the **SIL Open Font License, Version 1.1**
(<https://openfontlicense.org/>), which permits bundling and redistribution
within an application. Each family retains its own copyright and Reserved Font
Name as stated in its upstream `OFL.txt`.

| File(s)                                      | Family         | Copyright                                                                 |
| -------------------------------------------- | -------------- | ------------------------------------------------------------------------- |
| `Oswald-Medium.ttf`, `Oswald-SemiBold.ttf`   | Oswald         | Copyright 2016 The Oswald Project Authors (github.com/googlefonts/OswaldFont) |
| `Barlow-*.ttf`                               | Barlow         | Copyright 2017 The Barlow Project Authors (github.com/jpt/barlow)         |
| `JetBrainsMono-Regular.ttf`, `-Medium.ttf`   | JetBrains Mono | Copyright 2020 The JetBrains Mono Project Authors (github.com/JetBrains/JetBrainsMono) |

## Notes

- The Oswald files are **static weight instances** renamed to share the family
  name `Oswald` (OS/2 weight class 500 / 600). The upstream variable font is not
  used because variable-font weight realization fails in the headless Skia text
  backend used by the acceptance tests.
- Fonts are embedded via `<AvaloniaResource>` and referenced as
  `avares://Feuerwehr.App/Assets/Fonts#<Family>` in `Theme/Tokens.axaml`.
