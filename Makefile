# Common development tasks. Run `make` (or `make help`) for the target list.
#
# These targets only wrap the commands already documented in README.md,
# CONTRIBUTING.md, docker/README.md and .github/workflows/ — nothing here is a
# build system of its own, and CI deliberately does not call it.
#
# The Android head is built in Docker, never on the host: Microsoft.Android.Sdk
# rejects any JDK above 21 (error XA0030) and current distros ship newer ones.
# Everything after the build — emulator, install, logcat — uses the host
# Android SDK, because the build image has no emulator. `build` and `test`
# therefore run against LageBuch.Desktop.slnf, the solution minus that head;
# use `build-all` / `test-all` on a machine with a JDK <= 21.

DOTNET       ?= dotnet
CONFIG       ?= Debug
SLN          := LageBuch.sln
SLNF         := LageBuch.Desktop.slnf
APP          := src/LageBuch.App/LageBuch.App.csproj
ANDROID_PROJ := src/LageBuch.App.Android/LageBuch.App.Android.csproj
APP_ID       := de.codeforfire.lagebuch
FILTER       ?=
PROJECT      ?=
VERSION      ?= 0.1.0

ANDROID_HOME ?= $(HOME)/Android/Sdk
ADB          := $(ANDROID_HOME)/platform-tools/adb
EMULATOR_BIN := $(ANDROID_HOME)/emulator/emulator
AVD          ?= medium_tablet
IMAGE        ?= lagebuch-android-build
DOCKER_HOME  ?= $(HOME)/.cache/lagebuch-android-build
APK          := src/LageBuch.App.Android/bin/$(CONFIG)/net9.0-android/$(APP_ID)-Signed.apk

# Matches .github/workflows/release.yml's PUBLISH_FLAGS.
PUBLISH_FLAGS := -c Release --self-contained true -p:PublishSingleFile=true \
                 -p:IncludeNativeLibrariesForSelfExtract=true \
                 -p:DebugType=none -p:DebugSymbols=false

# xunit.v3 launches each test assembly as its own apphost, which fails with
# "You must install .NET to run this application" unless DOTNET_ROOT points at
# the runtime — not the case when the SDK lives outside the system prefix
# (~/.dotnet). Derive it from the dotnet actually on PATH; an existing
# DOTNET_ROOT in the environment wins.
export DOTNET_ROOT ?= $(patsubst %/,%,$(dir $(realpath $(shell command -v $(DOTNET)))))

TEST_FILTER := $(if $(FILTER),--filter $(FILTER),)
# FILTER on its own fails in assemblies that match nothing, so narrow with
# PROJECT=tests/LageBuch.Domain.Tests when filtering.
TEST_TARGET := $(if $(PROJECT),$(PROJECT),$(SLNF))

.DEFAULT_GOAL := help

.PHONY: help restore build build-all test test-all run format format-check ci clean \
        android-image android-image-rebuild apk emulator install run-android \
        logcat uninstall package-linux

help: ## Show this help
	@awk 'BEGIN {FS = ":.*## "} \
	     /^[a-zA-Z0-9_-]+:.*## / {printf "  \033[36m%-22s\033[0m %s\n", $$1, $$2} \
	     /^## / {printf "\n%s\n", substr($$0, 4)}' $(MAKEFILE_LIST)
	@echo ""
	@echo "  Variables: CONFIG=$(CONFIG) AVD=$(AVD) FILTER= PROJECT= VERSION=$(VERSION)"
	@echo "             ANDROID_HOME=$(ANDROID_HOME)"

## .NET (host)

restore: ## Restore NuGet packages
	$(DOTNET) restore $(SLNF)

build: ## Build everything except the Android head
	$(DOTNET) build $(SLNF) -c $(CONFIG)

build-all: ## Build the full solution incl. Android (needs JDK <= 21)
	$(DOTNET) build $(SLN) -c $(CONFIG)

test: ## Run tests (PROJECT=path and/or FILTER=Name~Foo to narrow)
	$(DOTNET) test $(TEST_TARGET) -c $(CONFIG) $(TEST_FILTER)

test-all: ## Run tests over the full solution (needs JDK <= 21)
	$(DOTNET) test $(SLN) -c $(CONFIG) $(TEST_FILTER)

run: ## Run the desktop app
	$(DOTNET) run --project $(APP)

# whitespace+style only: the analyzer pass is already a build gate here
# (AnalysisMode=All + TreatWarningsAsErrors), and `dotnet format analyzers`
# evaluates test projects differently from the build, reporting CA1515 on
# every public test class.
format: ## Apply dotnet format (whitespace + style)
	$(DOTNET) format whitespace $(SLNF)
	$(DOTNET) format style $(SLNF)

format-check: ## Fail if dotnet format would change anything
	$(DOTNET) format whitespace $(SLNF) --verify-no-changes
	$(DOTNET) format style $(SLNF) --verify-no-changes

ci: ## Reproduce the CI build+test legs locally
	$(DOTNET) restore $(SLNF)
	$(DOTNET) build $(SLNF) --configuration Release --no-restore
	$(DOTNET) test $(SLNF) --configuration Release --no-build --verbosity normal

clean: ## Remove build output (bin/ and obj/)
	-$(DOTNET) clean $(SLNF)
	find src tests -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +

## Android (built in Docker, run on the host)

android-image: ## Build the Android build image if it is missing
	@docker image inspect $(IMAGE) >/dev/null 2>&1 \
	  || docker build -f docker/android-build.Dockerfile -t $(IMAGE) docker/

android-image-rebuild: ## Rebuild the Android build image unconditionally
	docker build -f docker/android-build.Dockerfile -t $(IMAGE) docker/

# -u keeps bin/ and obj/ owned by the host user — a root-owned build tree breaks
# the next host `make build`, and only root can clear it again. That needs a
# writable HOME, which $(DOCKER_HOME) provides: a Docker named volume would be
# root-owned and unwritable, and a throwaway HOME would regenerate the Android
# debug keystore on every run, so each APK would be signed with a different key
# and `adb install -r` would reject the update. The host NuGet cache is mounted
# inside it so packages carry over from host builds.
apk: android-image ## Build an installable APK in Docker
	@mkdir -p "$(DOCKER_HOME)"
	docker run --rm \
	  -u $$(id -u):$$(id -g) \
	  -e HOME=/home/build -e DOTNET_CLI_HOME=/home/build \
	  -e DOTNET_NOLOGO=1 -e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
	  -v "$(DOCKER_HOME)":/home/build \
	  -v "$$HOME/.nuget":/home/build/.nuget \
	  -v "$$PWD":/src $(IMAGE) \
	  publish $(ANDROID_PROJ) -c $(CONFIG) -f net9.0-android \
	  -p:AndroidPackageFormat=apk -p:EmbedAssembliesIntoApk=true
	@echo "APK: $(APK)"

emulator: ## Boot the emulator (AVD=name) and wait for it
	@test -x "$(EMULATOR_BIN)" \
	  || { echo "No emulator at $(EMULATOR_BIN) — set ANDROID_HOME=/path/to/sdk"; exit 1; }
	@if $(ADB) devices | grep -q "device$$"; then \
	  echo "A device is already attached — skipping launch."; \
	else \
	  "$(EMULATOR_BIN)" -list-avds | grep -qx "$(AVD)" || { \
	    echo "AVD '$(AVD)' not found. Available: $$("$(EMULATOR_BIN)" -list-avds | tr '\n' ' ')"; \
	    echo "Create one with:"; \
	    echo "  $(ANDROID_HOME)/cmdline-tools/latest/bin/sdkmanager 'system-images;android-34;google_apis;x86_64'"; \
	    echo "  $(ANDROID_HOME)/cmdline-tools/latest/bin/avdmanager create avd -n $(AVD) -k 'system-images;android-34;google_apis;x86_64'"; \
	    exit 1; \
	  }; \
	  echo "Booting $(AVD)..."; \
	  "$(EMULATOR_BIN)" -avd $(AVD) >/dev/null 2>&1 & \
	  $(ADB) wait-for-device; \
	  until [ "$$($(ADB) shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')" = "1" ]; do sleep 2; done; \
	  echo "$(AVD) booted."; \
	fi

install: apk ## Install the APK on the attached device/emulator
	@$(ADB) install -r $(APK) || { \
	  echo "If this failed with INSTALL_FAILED_UPDATE_INCOMPATIBLE, the installed"; \
	  echo "copy was signed with a different debug key — run 'make uninstall' first."; \
	  exit 1; \
	}

# `monkey` with the launcher category rather than `am start -n .../MainActivity`:
# Avalonia.Android generates a CRC-prefixed activity name that is not stable.
run-android: emulator install ## Boot the emulator, install and launch the app
	$(ADB) shell monkey -p $(APP_ID) -c android.intent.category.LAUNCHER 1

logcat: ## Follow logcat for the running app
	@pid=$$($(ADB) shell pidof -s $(APP_ID) 2>/dev/null | tr -d '\r'); \
	if [ -z "$$pid" ]; then \
	  echo "$(APP_ID) is not running — start it with 'make run-android'."; exit 1; \
	fi; \
	$(ADB) logcat --pid=$$pid

uninstall: ## Remove the app from the attached device/emulator
	$(ADB) uninstall $(APP_ID)

## Packaging

package-linux: ## Build a local .deb (VERSION=x.y.z)
	$(DOTNET) publish $(APP) -r linux-x64 $(PUBLISH_FLAGS) -p:Version=$(VERSION) -o publish
	packaging/linux/build-deb.sh "$(VERSION)" publish \
	  src/LageBuch.App.Shared/Assets/icon-1024.png dist
