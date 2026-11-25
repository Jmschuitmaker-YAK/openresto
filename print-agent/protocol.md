# Print API v1 (proposed contract)

This document specifies the private versioned protocol implemented on the feature branch.
The integration remains unreleased until CI and staging validation.

All endpoints require HTTPS and an agent-specific bearer token. Store only a
hash of a high-entropy token server-side, scope it to one location and agent,
and support revocation. No admin session or guest credentials. Do not log tokens
or receipt payloads. Enforce rate limits and payload size limits.

## POST /api/print-agent/v1/claim

Request: {}. Response: 204 when idle; otherwise 200 with JSON:

```json
{"id":"immutable-job-id","lease":"opaque-lease-token","printer":"bar","text":"NIEUWE RESERVERING\n..."}
```

The server must durably enqueue jobs after confirmed booking creation,
cancellation and relevant modification (transactional outbox or equivalent).
Network and printer failures do not affect bookings. The SQL outbox shares the booking transaction, so a database/outbox write failure can still abort that transaction.
Each delivery has its own immutable job ID and event identity. Reprint is
an explicit new job, not a reset of an old ID. Snapshot the receipt text in
the restaurant's language/time zone, not the guest's UI language.
Printer aliases are configured on both sides; no arbitrary remote addresses.

Claim atomically for one authenticated agent; record the assignment and lease.
Lease duration at least 60 seconds. Expired jobs may be reclaimed only by the
same agent, with a new lease token. Never auto-transfer ambiguous jobs to a
different agent, including replacement devices with an empty journal.
Server must preserve terminal states and reject stale/foreign leases. A
single agent may have only one in-flight job; retries preserve queue order. Claim records heartbeat even
when no job exists.

## POST /api/print-agent/v1/result

```json
{"id":"immutable-job-id","lease":"opaque-lease-token","status":"sent"}
```

Statuses:
- sent: bytes accepted by the local TCP socket; physical paper output is not verified.
- retry: no printer bytes written; retry with bounded backoff and a visible error.
- uncertain: possible output or unreadable state; stop automatic retries and require review.

An optional reason is a short machine-readable code. Return 204 on success.
Make repeated identical results idempotent; a lost response must not cause a
second print. The agent can reclaim the same ID and report its journaled
result. Do not return a terminal job as a new job with a different ID.
Keep failed/uncertain jobs visible with operator reprint and audit trail.
Retention of both server queue data and agent journal must be coordinated.

## Admin integration

Independent email and printer toggles for internal alerts; guest email and
push remain unchanged. Default printer off for existing installations.
Initial scope: one active agent per location, alias, token registration/revocation,
last heartbeat, queued/sent/uncertain status and test receipt. Explicit reprint creates a marked new job from the current booking, never replays a stale snapshot. Advanced event configuration is follow-up work.
Use least-privilege access and minimize receipt PII; guest contact data and
notes should be printed only when operationally necessary.
