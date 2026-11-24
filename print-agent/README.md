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
  can be replayed. Journal retention/compaction needs an API retention agreement.
- Initial target: Linux (systemd), TCP ESC/POS printers. Other OS builds are
  experimental; Windows service installation and durability need further work.
- Initial receipts are portable ASCII (common accents transliterated); printer
  code-page support and layout need testing with actual equipment.

## Local development

From this directory: `go test ./...`, `go build ./...`.
Use a fake TCP printer and mock API until the server integration exists.
See protocol.md for the required API contract.

## Future Linux installation

On a staging system after building the agent: install the binary
to /usr/local/bin/openresto-print-agent, create /etc/openresto-print-agent,
copy config.example.json to config.json, set the HTTPS origin and printer mapping,
and set tokenFile to /run/credentials/openresto-print-agent.service/token.
Store the token in /etc/openresto-print-agent/token (root-only, mode 0600).
The config contains no credentials and may be read by the systemd dynamic user.
Install the included unit in /etc/systemd/system, then enable it with systemctl.
Do not install against the current production API.
