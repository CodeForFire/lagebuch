# Builds the Android head (src/LageBuch.App.Android) with a JDK the .NET
# Android SDK actually supports. Microsoft.Android.Sdk currently rejects any
# JDK above 21 (error XA0030), and several current Linux distros (e.g. Debian
# testing/sid) no longer package any JDK as old as 21 —
# see docker/README.md for usage and rationale.

FROM mcr.microsoft.com/dotnet/sdk:10.0

# Eclipse Temurin JDK 21 via Adoptium's apt repo. Codename is read from
# /etc/os-release rather than hardcoded — the dotnet/sdk base image's own
# underlying distro has changed before (Debian bookworm for .NET 9, Ubuntu
# noble for .NET 10) and will likely change again.
RUN apt-get update \
    && apt-get install -y --no-install-recommends wget gnupg unzip \
    && wget -qO- https://packages.adoptium.net/artifactory/api/gpg/key/public \
       | gpg --dearmor -o /usr/share/keyrings/adoptium.gpg \
    && . /etc/os-release \
    && echo "deb [signed-by=/usr/share/keyrings/adoptium.gpg] https://packages.adoptium.net/artifactory/deb ${VERSION_CODENAME} main" \
       > /etc/apt/sources.list.d/adoptium.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends temurin-21-jdk \
    && rm -rf /var/lib/apt/lists/*

ENV JAVA_HOME=/usr/lib/jvm/temurin-21-jdk-amd64
ENV PATH="${JAVA_HOME}/bin:${PATH}"

# Android SDK: command-line tools + just enough packages to compile
# (no emulator/system-images — this image is build-only, run on the host).
ENV ANDROID_HOME=/opt/android-sdk
ENV ANDROID_SDK_ROOT=${ANDROID_HOME}
RUN mkdir -p ${ANDROID_HOME}/cmdline-tools \
    && cd ${ANDROID_HOME}/cmdline-tools \
    && wget -q https://dl.google.com/android/repository/commandlinetools-linux-16111833_latest.zip -O cmdline-tools.zip \
    && unzip -q cmdline-tools.zip \
    && rm cmdline-tools.zip \
    && mv cmdline-tools latest
ENV PATH="${ANDROID_HOME}/cmdline-tools/latest/bin:${ANDROID_HOME}/platform-tools:${PATH}"
# android-36 is required because the .NET 10 Android workload compiles
# against the latest API it bundles for net10.0-android36.0, regardless of
# targetSdkVersion (error XA5207 otherwise).
RUN android sdk install platform-tools \
    "platforms/android-34" "build-tools/34.0.0" \
    "platforms/android-36" "build-tools/36.0.0"

RUN dotnet workload install android

# The SDK unpacks its tools mode 0744, so only root can execute zipalign/aapt2
# and friends. The Makefile runs this image as the invoking host UID, so that
# builds do not leave root-owned bin/ and obj/ in the mounted work tree — which
# needs the toolchain readable and executable by everyone.
RUN chmod -R a+rX ${ANDROID_HOME}

WORKDIR /src
ENTRYPOINT ["dotnet"]
