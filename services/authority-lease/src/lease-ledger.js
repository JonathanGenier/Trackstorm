export const LEASE_MS = 10_000;
export const OPERATIONS = new Set(["create", "read", "renew", "takeover", "release"]);

export function validRequest(request) {
  return request !== null && typeof request === "object" &&
    Object.keys(request).length === 3 &&
    typeof request.session === "string" && /^[A-Fa-f0-9]{64}$/.test(request.session) &&
    Number.isSafeInteger(request.epoch) && request.epoch >= 0 &&
    typeof request.token === "string" &&
    (request.token === "" || /^[a-f0-9]{32}$/.test(request.token));
}

function validateRecord(record) {
  if (record !== undefined && (!record || Object.keys(record).length !== 5 ||
      !validRequest({ session: record.session, epoch: record.epoch, token: record.token }) || record.epoch === 0 || record.token === "" ||
      typeof record.holder !== "string" || record.holder.length === 0 || record.holder.length > 256 || /\s/.test(record.holder) ||
      !Number.isSafeInteger(record.expiresAt) || record.expiresAt < 0)) {
    throw new Error("Invalid lease state.");
  }
}

// One record in one session's Durable Object. No gameplay or election data enters here.
export class LeaseLedger {
  constructor(storage, now = Date.now) {
    this.storage = storage;
    this.now = now;
    this.renewableEpoch = 0;
  }

  async restart() {
    const current = await this.storage.get("lease");
    validateRecord(current);
    if (current) {
      // A new object lifetime never shortens a previous holder's possible permission.
      // Old renewals stay disabled; a fresh election/takeover is required after quarantine.
      await this.storage.put("lease", { ...current, expiresAt: Math.max(current.expiresAt, this.now() + LEASE_MS) });
    }
  }

  async operate(operation, request, subject) {
    if (!OPERATIONS.has(operation) || !validRequest(request) ||
        typeof subject !== "string" || subject.length === 0 || subject.length > 256 || /\s/.test(subject)) {
      return null;
    }

    const result = await this.storage.transaction(async (transaction) => {
      const current = await transaction.get("lease");
      validateRecord(current);
      const now = this.now();
      let next;
      if (operation === "create") {
        if (current || request.epoch !== 0 || request.token !== "") return null;
        next = { session: request.session, holder: subject, epoch: 1 };
      } else {
        if (!current || current.session !== request.session) return null;
        if (operation === "read") return this.snapshot(current, now);
        if (current.epoch !== request.epoch || current.token !== request.token) return null;
        if (operation === "renew") {
          if (current.holder !== subject || current.expiresAt <= now || this.renewableEpoch !== current.epoch) return null;
          next = current;
        } else if (operation === "release") {
          if (current.holder !== subject) return null;
          next = { ...current, expiresAt: 0 };
          await transaction.put("lease", next);
          return this.snapshot(next, now);
        } else {
          if (current.holder === subject || current.expiresAt > now || current.epoch === Number.MAX_SAFE_INTEGER) return null;
          next = { ...current, holder: subject, epoch: current.epoch + 1 };
        }
      }

      next = { ...next, token: crypto.randomUUID().replaceAll("-", ""), expiresAt: now + LEASE_MS };
      await transaction.put("lease", next);
      return this.snapshot(next, now);
    });

    if (result && ["create", "renew", "takeover"].includes(operation)) this.renewableEpoch = Math.max(this.renewableEpoch, result.epoch);
    return result;
  }

  snapshot(record, now) {
    const { expiresAt, ...grant } = record;
    return { ...grant, remainingSeconds: Math.max(0, Math.min(LEASE_MS, expiresAt - now)) / 1000 };
  }

  async route() {
    const current = await this.storage.get("lease");
    validateRecord(current);
    if (!current) return null;
    const { holder, epoch, remainingSeconds } = this.snapshot(current, this.now());
    return { holder, epoch, remainingSeconds };
  }
}
