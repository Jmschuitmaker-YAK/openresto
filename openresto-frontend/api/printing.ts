import { get, post } from "./client";

export interface PrintAgentStatus {
  agentId: string;
  printer: string;
  locale: string;
  enabled: boolean;
  lastSeen: number | null;
}

export interface PrintJobStatus {
  id: string;
  agentId: string;
  kind: "created" | "cancelled" | "changed" | "test";
  status: "queued" | "leased" | "sent" | "uncertain";
  attempts: number;
  reason: string | null;
  createdAt: number;
  bookingRef: string;
  canReprint: boolean;
}

export interface PrintStatus {
  agent: PrintAgentStatus | null;
  jobs: PrintJobStatus[];
}

const path = (id: number) => `/admin/printing/${id}`;

export async function getPrintStatus(id: number): Promise<PrintStatus> {
  const response = await get(path(id));
  if (!response.ok) throw new Error("Print status unavailable");
  return response.json();
}

export async function registerPrintAgent(
  id: number,
  printer: string,
  locale: string,
  replace: boolean
): Promise<{ agentId: string; token: string }> {
  const response = await post(`${path(id)}/register`, { printer, locale, replace });
  if (!response.ok) throw new Error("Agent registration failed");
  return response.json();
}

export async function printAction(
  id: number,
  action: "enabled" | "revoke" | "test",
  body: { enabled: boolean } | { confirm: boolean }
): Promise<void> {
  const response = await post(`${path(id)}/${action}`, body);
  if (!response.ok) throw new Error("Print action failed");
}

export async function reprintBooking(id: number, jobId: string): Promise<void> {
  const response = await post(`${path(id)}/reprint/${encodeURIComponent(jobId)}`, { confirm: true });
  if (!response.ok) throw new Error("Reprint failed");
}
