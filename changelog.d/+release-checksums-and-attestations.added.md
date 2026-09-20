Every release now ships `SHA256SUMS.txt` and a Sigstore-backed build attestation for each
installer, so a download can be verified with `sha256sum -c` and
`gh attestation verify <Datei> --repo CodeForFire/lagebuch` while the packages are still
unsigned. The macOS `.dmg`, which is attached later, carries its own `.sha256` file.
