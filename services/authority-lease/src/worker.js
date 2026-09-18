import { DurableObject } from "cloudflare:workers";
import { authenticate, configured } from "./eos-identity.js";
import { LeaseLedger, OPERATIONS, validRequest } from "./lease-ledger.js";
import joseLicense from "./jose-license.js";

// Only this Worker binding can invoke the object. Public callers cannot supply a holder.
export class LeaseSession extends DurableObject {
  constructor(ctx, env) {
    super(ctx, env);
    this.ledger = new LeaseLedger(ctx.storage);
    ctx.blockConcurrencyWhile(() => this.ledger.restart());
  }

  async operate(operation, request, subject) {
    return this.ledger.operate(operation, request, subject);
  }

  async route() {
    return this.ledger.route();
  }
}

function reply(status, body) {
  return Response.json(body, { status, headers: { "Cache-Control": "no-store" } });
}

async function readRequest(request) {
  if (!request.body || request.headers.get("Content-Type")?.split(";")[0].trim() !== "application/json") return null;
  const reader = request.body.getReader();
  let length = 0;
  const chunks = [];
  for (;;) {
    const { done, value } = await reader.read();
    if (done) break;
    length += value.byteLength;
    if (length > 2048) { await reader.cancel(); return null; }
    chunks.push(value);
  }
  const bytes = new Uint8Array(length);
  let offset = 0;
  for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.byteLength; }
  try { return JSON.parse(new TextDecoder("utf-8", { fatal: true }).decode(bytes)); } catch { return null; }
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (url.protocol !== "https:" || url.search) return reply(400, { error: "Invalid request." });
    if (request.method === "GET" && url.pathname === "/health") return reply(200, { status: "up" });
    if (request.method === "GET" && url.pathname === "/licenses") return new Response(joseLicense, { headers: { "Content-Type": "text/plain; charset=utf-8", "Cache-Control": "no-store" } });
    const operation = url.pathname.match(/^\/lease\/([a-z]+)$/)?.[1];
    if (request.method !== "POST" || (!OPERATIONS.has(operation) && operation !== "route")) return reply(404, { error: "Not found." });
    if (!configured(env)) return reply(503, { error: "Coordination unavailable." });
    let subject;
    try { subject = await authenticate(request.headers.get("Authorization"), env); }
    catch { return reply(401, { error: "Authentication unavailable or rejected." }); }

    try {
      const body = await readRequest(request);
      if (!validRequest(body)) return reply(400, { error: "Invalid request." });
      if (operation === "route") {
        if (body.epoch !== 0 || body.token !== "") return reply(400, { error: "Invalid request." });
        const id = env.LEASE_SESSIONS.idFromString(body.session);
        const route = await env.LEASE_SESSIONS.get(id).route();
        return route ? reply(200, { routingId: id.toString(), ...route }) : reply(409, { error: "Lease conflict." });
      }
      const id = env.LEASE_SESSIONS.idFromName(body.session);
      const result = await env.LEASE_SESSIONS.get(id).operate(operation, body, subject);
      return result ? reply(200, { ...result, routingId: id.toString() }) : reply(409, { error: "Lease conflict." });
    } catch {
      // Never expose tokens, session IDs, provider errors or request bodies to logs/responses.
      return reply(503, { error: "Coordination unavailable." });
    }
  },
};
