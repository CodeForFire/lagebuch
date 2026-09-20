The download-verification instructions no longer send macOS users to a file that cannot
contain their download. `SHA256SUMS.txt` covers the `.msi`, the `.deb` and the `.apk`; the
`.dmg` is built after the release exists and carries its own `.dmg.sha256`, so
`shasum -a 256 -c SHA256SUMS.txt` — the only line the README and the release notes offered a
Mac user — failed on every one of the three files it listed. Both now name the `.dmg.sha256`
path as well.
