# Builds the Android head (src/LageBuch.App.Android) with a JDK the .NET
# Android SDK actually supports. Microsoft.Android.Sdk currently rejects any
# JDK above 21 (error XA0030), and several current Linux distros (e.g. Debian
# testing/sid) no longer package anything below JDK 21+ builds beyond it —
# see docker/README.md for usage and rationale.

FROM mcr.microsoft.com/dotnet/sdk:9.0

# Eclipse Temurin JDK 21 via Adoptium's apt repo (matches this image's
# Debian bookworm base).
RUN apt-get update \
    && apt-get install -y --no-install-recommends wget gnupg unzip \
    && wget -qO- https://packages.adoptium.net/artifactory/api/gpg/key/public \
       | gpg --dearmor -o /usr/share/keyrings/adoptium.gpg \
    && echo "deb [signed-by=/usr/share/keyrings/adoptium.gpg] https://packages.adoptium.net/artifactory/deb bookworm main" \
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
# android-35 is required even though the app targets API 34: the .NET 9
# Android workload (Microsoft.Android.Sdk.Linux 35.0.105) compiles against
# the latest API it bundles (error XA5207 otherwise).
RUN android sdk install platform-tools \
    "platforms/android-34" "build-tools/34.0.0" \
    "platforms/android-35" "build-tools/35.0.0"

RUN dotnet workload install android

# The SDK unpacks its tools mode 0744, so only root can execute zipalign/aapt2
# and friends. The Makefile runs this image as the invoking host UID, so that
# builds do not leave root-owned bin/ and obj/ in the mounted work tree — which
# needs the toolchain readable and executable by everyone.
RUN chmod -R a+rX ${ANDROID_HOME}

WORKDIR /src
ENTRYPOINT ["dotnet"]
