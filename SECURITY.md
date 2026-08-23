# Security Policy

## Supported versions

Lagebuch is pre-1.0 and moves quickly. Only the **latest release** receives
security fixes; because the project has not reached 1.0, breaking changes
between versions are expected and older releases are not patched.

| Version | Supported |
|---------|-----------|
| latest release | :white_check_mark: |
| earlier releases | :x: |

## Reporting a vulnerability

**Please do not report security vulnerabilities through public GitHub issues,
discussions, or pull requests.**

Use [GitHub's private vulnerability reporting](https://docs.github.com/en/code-security/security-advisories/guidance-on-reporting-and-writing-information-about-vulnerabilities/privately-reporting-a-security-vulnerability)
instead:

1. Go to the repository's **Security** tab.
2. Click **Report a vulnerability**.
3. Describe the issue, including:
   - the affected version (installer file name or tag, e.g. `v0.3.0`)
   - platform(s) affected — Windows, Linux, macOS, Android
   - steps to reproduce or a proof of concept
   - the impact you believe it has

Reports are reviewed promptly. Please give maintainers reasonable time to
address the issue before any public disclosure.

## Scope notes

- **Master data and incident files stay on device.** `masterdata.db` and each
  incident's `.fwincident` file live in local application data; nothing is
  uploaded by the app itself.
- **Multi-device sync** runs over LAN/Tailscale via SignalR and requires a
  share PIN to join an incident. Issues affecting that transport, the PIN gate,
  or the PDF export pipeline are very much in scope.

## Non-security issues

Anything that is not a vulnerability (crashes, data-entry problems, feature
requests) belongs in the [issue tracker](../../issues) using the regular issue
templates.
