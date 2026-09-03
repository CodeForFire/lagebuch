# Android build container

`android-build.Dockerfile` builds the Android head
(`src/LageBuch.App.Android`) with Eclipse Temurin JDK 21 + the Android SDK,
for machines where the system JDK is newer than what `Microsoft.Android.Sdk`
currently supports (it hard-rejects anything above JDK 21 — error `XA0030` —
while several current distros, e.g. Debian testing/sid, no longer package
anything that old).

This image is **build-only** — it has no emulator. Run/deploy against a
device or emulator on the host as usual (`adb install`, or set up an
emulator per the main README/CONTRIBUTING once a host JDK ≤ 21 is available).

`make apk` wraps both steps below (building the image on first use) and runs
the container as the invoking user, so the build does not leave root-owned
`bin/` and `obj/` directories in the work tree. The raw commands follow for
reference.

## Build the image

```bash
docker build -f docker/android-build.Dockerfile -t lagebuch-android-build docker/
```

## Build the app

Run from the repo root; the container mounts the repo at `/src` and forwards
its argument straight to `dotnet`:

```bash
docker run --rm -v "$PWD":/src lagebuch-android-build \
  build src/LageBuch.App.Android/LageBuch.App.Android.csproj -c Debug -f net10.0-android
```

Produce an installable `.apk` (add `EmbedAssembliesIntoApk=true` for a Debug
build so it's self-contained for a plain `adb install` — without it, Debug
builds rely on the IDE's incremental deploy to push assemblies separately,
and a sideloaded APK will crash at startup with "No assemblies found"):

```bash
docker run --rm -v "$PWD":/src lagebuch-android-build \
  publish src/LageBuch.App.Android/LageBuch.App.Android.csproj -c Debug -f net10.0-android \
  -p:AndroidPackageFormat=apk -p:EmbedAssembliesIntoApk=true
```

The APK lands at
`src/LageBuch.App.Android/bin/Debug/net10.0-android/de.codeforfire.lagebuch-Signed.apk`.
Install it to a running emulator or connected device with:

```bash
adb install -r src/LageBuch.App.Android/bin/Debug/net10.0-android/de.codeforfire.lagebuch-Signed.apk
```

## Notes

- The `commandlinetools-linux-*_latest.zip` build number in the Dockerfile
  is pinned to what was current at the time of writing. If a future
  `sdkmanager`/`android sdk` invocation fails oddly, check
  https://developer.android.com/studio#command-tools for the current build
  and bump it.
- `platforms/android-36` + `build-tools/36.0.0` are installed alongside 34
  because the .NET 10 Android workload compiles against the latest API
  level it bundles for `net10.0-android36.0`, regardless of the app's
  `targetSdkVersion` (error `XA5207` otherwise).
