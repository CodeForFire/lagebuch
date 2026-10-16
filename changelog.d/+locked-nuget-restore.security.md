CI now restores NuGet packages against committed lock files with content hashes and installs its Python tooling with pinned hashes, so a tampered or substituted dependency fails the build.
