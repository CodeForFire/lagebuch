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

- **Master data and incident files stay on device**, with one exception: when
  a device hosts an incident, it serves its full master-data set — including
  the personnel roster's names and phone numbers — to every device that joins
  that incident, over the same TLS + share-PIN-gated channel as everything
  else. A joining device never persists what it receives; nothing is uploaded
  by the app itself. Note that the roster is standing organizational data (the
  whole brigade, across every incident), unlike the incident payload, which is
  a single event — so a compromised PIN exposes something with a longer useful
  life than the operation the PIN was issued for. `masterdata.db` and each
  incident's `.fwincident` file otherwise live in local application data.
- **Multi-device sync** runs over LAN/Tailscale via SignalR and requires a
  share PIN to join an incident. Issues affecting that transport, the PIN gate,
  or the PDF export pipeline are very much in scope.

## Known limitations

- **Operator identity is self-asserted per device, not verified.** The
  "Wer dokumentiert?" name/call sign attached to each sync command travels
  with that command and is trusted by the host as-is — there is no
  server-side lookup against any registered or authenticated identity. It is
  used **only** for display and attribution (an ETB entry's "entered by",
  an incident's "closed by", a file's "added by" metadata), never as an
  authorization gate: once a device is past the PIN gate, every operator
  name can perform every mutation.
- **The PIN plus network reachability is the entire access-control
  boundary.** A device that can reach the host over the LAN/Tailscale link
  and knows the (rate-limited, TLS-protected) PIN is already fully trusted
  to make arbitrary changes to the incident, regardless of what operator
  name it claims. A compromised or careless device can misattribute its own
  edits to a different operator, but it could just as easily make those
  edits under its own name — the trust decision has already been made by
  that point.
- **This is an accepted trade-off, not an oversight.** The app's threat
  model is a volunteer fire department's own devices on its own network,
  not an adversarial multi-tenant system. Verifying operator identity
  server-side would require inventing a session/identity-binding concept
  that doesn't exist today — the SignalR hub carries no client-callable
  methods, and sync commands travel over stateless HTTP POST — for a
  marginal reduction of an already low-severity risk. If stronger
  attribution is ever wanted, a cheap next step would be recording the
  source device/IP alongside the claimed operator name in ETB entries,
  without requiring a full identity-binding redesign.

## Non-security issues

Anything that is not a vulnerability (crashes, data-entry problems, feature
requests) belongs in the [issue tracker](../../issues) using the regular issue
templates.
