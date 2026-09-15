Linux: the `.deb` now declares the system libraries it actually needs. "Self-contained"
covers the .NET runtime, not ICU, fontconfig and the X11 client libraries — so on a machine
without a desktop environment already installed, `apt install ./lagebuch_*.deb` reported
success and the app then died immediately with "Couldn't find a valid ICU package installed
on the system". Install it with `apt` rather than `dpkg -i`, which cannot resolve
dependencies. Verified on Debian 12/13 and Ubuntu 22.04/24.04.
