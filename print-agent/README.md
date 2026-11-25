# OpenResto Print Agent — prototype

A small Go agent for reservation receipts, running independently of the POS.
This is an **unreleased integration prototype**. The branch includes a server outbox and
private protocol; CI, staging and hardware verification are required before production.
Pair a fresh agent identity with each new installation.

## Design

- The agent makes outbound authenticated HTTPS requests only.
- Printers are mapped by local aliases (e.g. bar). The server cannot supply a
  network address or raw ESC/POS commands.
- A local durable journal records intent before the first printer write and
  remembers the terminal outcome by immutable job ID. Never automatically reprint
  an ambiguous delivery. TCP acceptance does not prove a physical receipt printed.
- An offline printer before any write is retryable. A partial write or interrupted
  process is uncertain and needs operator review.
- Journal records contain only job IDs, status and error codes, not guest details.
- Run exactly one process per agent credential and state directory. The agent locks
  its state directory; never reuse a credential on a different machine. Do not delete the journal while jobs
  can be replayed. Server jobs expire after 7 days and terminal history after 30 days. Local journal compaction is not yet implemented.
- Initial target: Linux (systemd), TCP ESC/POS printers. Other OS builds are
  experimental; Windows service installation and durability need further work.
- Initial receipts are portable ASCII (common accents transliterated); printer
  code-page support and layout need testing with actual equipment.

## Local development

From this directory: `go test ./...`, `go build ./...`.
Use a fake TCP printer and mock API for local unit tests. CI produces Linux test binaries and SHA256SUMS.
See protocol.md for the required API contract.

## Linux staging installation

On a staging system after building the agent: install the binary
to /usr/local/bin/openresto-print-agent, create /etc/openresto-print-agent,
copy config.example.json to config.json, set the HTTPS origin and printer mapping,
and set tokenFile to /run/credentials/openresto-print-agent.service/token.
Store the token in /etc/openresto-print-agent/token (root-only, mode 0600).
The config contains no credentials and may be read by the systemd dynamic user.
Install the included unit in /etc/systemd/system, then enable it with systemctl.
Do not install against the current production API.

## Acceptance checklist

1. Use a separate staging instance and fresh database. Confirm the server's HTTPS
   certificate is valid; do not bypass TLS verification.
2. In admin Email & Push, register the agent with the local alias, select receipt
   language, keep automatic printing disabled, and copy the one-time token to the
   protected file. Use an unused state directory for this new identity.
3. Start the service. Refresh status; the agent should connect. Send one test receipt.
   Verify the physical paper (sent status alone is not proof).
4. Enable automatic printing on staging only. Create, change and cancel a dummy
   booking; verify each receipt, language and local time.
5. Disconnect the printer, create a dummy booking, then reconnect. Verify retry and
   original order. Stop/start the agent and server; verify the queue survives.
6. Verify that an uncertain receipt requires explicit operator review; an approved
   reprint has a new identity and a visible REPRINT heading.
7. Revoke the staging agent and verify polling is refused. Restore production only
   through a separately approved deployment. Never share a token between machines.

