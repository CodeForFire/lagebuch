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
- **The full data-protection picture** — every category of personal data, all
  storage locations, what the sync transmits, the split of technical and
  organizational measures, and the deletion checklist — is documented in
  German in
  [`docs/datenschutz-und-sicherheit.md`](docs/datenschutz-und-sicherheit.md),
  written for the Kreisbrandinspektionen and Datenschutzbeauftragte who have
  to approve a deployment.
- **Multi-device sync** runs over LAN/Tailscale via SignalR and requires a
  share PIN to join an incident. Issues affecting that transport, the PIN gate,
  or the PDF export pipeline are very much in scope.

## Known limitations

The German
[`docs/datenschutz-und-sicherheit.md`](docs/datenschutz-und-sicherheit.md)
lists the same limits under "Bekannte Grenzen". This is that list, in that
order, cut for a researcher rather than for a Datenschutzbeauftragter, plus
one note on why the trust model around the middle two is where it is.

- **The installers are not code-signed.** Every install path makes the user
  click past an operating-system warning — SmartScreen on Windows, quarantine
  on macOS, unknown sources on Android — so there is no OS-level integrity
  check on the package, and the flow trains users to dismiss exactly the
  dialog that would flag a tampered one. The SignPath Foundation reviewed the
  project and declined; OSSign requires six months of account, organisation
  and project history, which puts the earliest possible free Windows
  certificate at February 2027. Until then the verifiable substitutes are each
  release's `SHA256SUMS.txt` and its Sigstore-backed build attestation
  (`gh attestation verify <file> --repo CodeForFire/lagebuch`). See the
  [roadmap](ROADMAP.md).
- **The first connection to a host is unauthenticated.** Sync pins the host
  certificate Trust-on-First-Use: the joining device records its SHA-256
  thumbprint in `trust.json` on first contact and from then on accepts only
  that thumbprint: a changed one aborts the connection, and there is no
  fallback to an unverified one. The pin can be reset — "forget" on a host
  drops its entry, which is what makes a legitimately reissued certificate
  usable — so that action is itself the downgrade path back to an
  unauthenticated first contact. That thumbprint comparison is the *whole*
  client-side trust
  decision — the per-share certificate is self-signed and its SAN covers only
  `localhost` and loopback while the host binds every interface, so chain and
  hostname validation are replaced outright rather than layered on. The
  thumbprint is never surfaced in the UI either, so the first exchange cannot
  be compared against the host out of band: an attacker already positioned on
  the LAN at that moment can get themselves pinned instead, and every later
  connection will then look correct.
  ([#288](../../issues/288))
- **The share PIN is four digits.** It is drawn per share from
  `RandomNumberGenerator`, held in memory only, never persisted, and a wrong
  PIN puts the offending source IP into an exponential backoff
  (2^(failures-1) seconds, capped at 60). But the space is 10,000 values, and
  the backoff is keyed per source IP, is held in memory only for the host
  process's lifetime, never locks an address out, and lumps every peer whose
  remote address does not resolve into one shared bucket — enough against
  someone guessing, not against an attacker with network access, time and more
  than one address. Six digits are part of the same issue as the thumbprint
  display ([#288](../../issues/288)).
- **The PIN plus network reachability is the entire access-control
  boundary.** A device that can reach the host over the LAN/Tailscale link
  and knows the (rate-limited, TLS-protected) PIN is already fully trusted
  to make arbitrary changes to the incident, regardless of what operator
  name it claims. A compromised or careless device can misattribute its own
  edits to a different operator, but it could just as easily make those
  edits under its own name — the trust decision has already been made by
  that point. The host also listens on every interface (`ListenAnyIP`), with
  no restriction to particular address ranges.
- **Operator identity is self-asserted per device, not verified.** The
  "Wer dokumentiert?" name/call sign attached to each sync command travels
  with that command and is trusted by the host as-is — there is no
  server-side lookup against any registered or authenticated identity. It is
  used **only** for display and attribution (an ETB entry's "entered by",
  an incident's "closed by", a file's "added by" metadata), never as an
  authorization gate: once a device is past the PIN gate, every operator
  name can perform every mutation.
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
- **Image metadata is not stripped from attachments.** Image bytes are stored
  verbatim, so a photo taken on a duty phone keeps whatever the camera wrote
  into it — GPS coordinates, capture time, device model — inside the incident's
  `.files` folder, in what the host serves to every joined device, and in the
  exported PDF report that goes to the Akte. The coordinates are typically a
  private address, often the same one whose resident is already named in the
  CO-Messprotokoll. PNG, WebP and GIF metadata chunks are equally untouched.
  Stripping on ingest is planned
  ([#384](../../issues/384)).
- **Attachment copies outlive the session that created them.** A joining
  device keeps every attachment it pulled in `attachment-cache/` after the
  connection ends, and the cache is never cleared automatically
  ([#382](../../issues/382)); opening an
  attachment additionally leaves a working copy under `lagebuch/` in the
  system temp directory, one fresh directory per open, also never cleaned up
  ([#383](../../issues/383)). Both are
  plaintext copies of personal data outside the incident file, so both are on
  the German page's deletion checklist.
- **The file format is not frozen before 1.0.** Older `.fwincident` files are
  migrated forward on open, and the guarantee that a file stays readable
  indefinitely starts at 1.0, not now.
- **There are no log files.** Good for data protection — no second place on
  disk accumulates personal data — and bad for diagnosis: a crash leaves
  nothing behind to attach to a report, including a crash in the sync or
  export path that a reporter would want evidence for.

## Non-security issues

Anything that is not a vulnerability (crashes, data-entry problems, feature
requests) belongs in the [issue tracker](../../issues) using the regular issue
templates.
