import test from "node:test";
import assert from "node:assert/strict";
import { LeaseLedger, LEASE_MS } from "../src/lease-ledger.js";

// Transactional test double with rollback and a controlled clock. Actual SQLite DO
// routing, transactions and authentication are also exercised in worker.test.js.
class Storage {
  data = new Map();
  tail = Promise.resolve();
  offline = false;
  async get(key) { return structuredClone(this.data.get(key)); }
  async put(key, value) { if (this.offline) throw new Error("offline"); this.data.set(key, structuredClone(value)); }
  transaction(callback) {
    const task = this.tail.then(async () => {
      if (this.offline) throw new Error("offline");
      const copy = new Map(this.data);
      const result = await callback({
        get: async key => structuredClone(copy.get(key)),
        put: async (key, value) => { if (this.offline) throw new Error("offline"); copy.set(key, structuredClone(value)); },
      });
      this.data = copy;
      return result;
    });
    this.tail = task.catch(() => {});
    return task;
  }
}
const session = "A".repeat(64);
const initial = { session, epoch: 0, token: "" };
const fence = grant => ({ session: grant.session, epoch: grant.epoch, token: grant.token });
function setup() {
  let now = 100_000;
  const storage = new Storage();
  const ledger = new LeaseLedger(storage, () => now);
  return { ledger, storage, now: () => now, advance: ms => { now += ms; } };
}

test("normal renewal keeps the healthy partitioned host authoritative", async () => {
  const { ledger, advance } = setup();
  let grant = await ledger.operate("create", initial, "host");
  assert.equal(grant.epoch, 1);
  assert.equal(grant.remainingSeconds, 10);
  assert.equal(await ledger.operate("create", initial, "other"), null);
  for (let i = 0; i < 20; i++) {
    advance(2000);
    const previous = grant;
    grant = await ledger.operate("renew", fence(previous), "host");
    assert.notEqual(grant.token, previous.token);
    assert.equal(await ledger.operate("takeover", fence(grant), "client"), null);
    assert.equal(await ledger.operate("renew", fence(previous), "host"), null);
    assert.equal(await ledger.operate("release", fence(previous), "host"), null);
  }
  assert.equal((await ledger.operate("read", initial, "client")).holder, "host");
});

test("crash expiry is exact, concurrent takeover has one winner and epochs advance once", async () => {
  const { ledger, advance } = setup();
  const old = await ledger.operate("create", initial, "host");
  advance(LEASE_MS - 1);
  assert.equal(await ledger.operate("takeover", fence(old), "client"), null);
  advance(1);
  assert.equal(await ledger.operate("renew", fence(old), "host"), null);
  const claims = await Promise.all(Array.from({ length: 16 }, (_, i) => ledger.operate("takeover", fence(old), `client-${i}`)));
  const winners = claims.filter(Boolean);
  assert.equal(winners.length, 1);
  const second = winners[0];
  assert.equal(second.epoch, 2);
  for (const operation of ["renew", "release", "takeover"]) assert.equal(await ledger.operate(operation, fence(old), "host"), null);
  advance(LEASE_MS);
  assert.equal(await ledger.operate("takeover", fence(second), second.holder), null);
  const third = await ledger.operate("takeover", fence(second), "host");
  assert.equal(third.epoch, 3); // New explicit election only; return/resume never calls this operation.
  assert.notEqual(third.token, second.token);
  assert.equal(await ledger.operate("renew", fence(second), second.holder), null);
});

test("release is conditional, persists expiry and never resets the epoch", async () => {
  const { ledger } = setup();
  const old = await ledger.operate("create", initial, "host");
  assert.equal(await ledger.operate("release", fence(old), "other"), null);
  assert.equal((await ledger.operate("release", fence(old), "host")).remainingSeconds, 0);
  assert.equal(await ledger.operate("renew", fence(old), "host"), null);
  const next = await ledger.operate("takeover", fence(old), "other");
  assert.equal(next.epoch, 2);
  assert.equal(await ledger.operate("release", fence(old), "host"), null);
  assert.equal(await ledger.operate("create", initial, "host"), null);
});

test("storage outage grants no authority or replacement", async () => {
  const { ledger, storage, advance } = setup();
  const old = await ledger.operate("create", initial, "host");
  storage.offline = true;
  await assert.rejects(ledger.operate("renew", fence(old), "host"));
  advance(LEASE_MS);
  await assert.rejects(ledger.operate("takeover", fence(old), "client"));
  storage.offline = false;
  assert.equal((await ledger.operate("read", initial, "client")).epoch, 1);
});

test("object restart quarantines grants and rejects prior-lifetime renewal", async () => {
  const setupState = setup();
  const old = await setupState.ledger.operate("create", initial, "host");
  setupState.advance(2000);
  const restarted = new LeaseLedger(setupState.storage, setupState.now);
  await restarted.restart();
  assert.equal(await restarted.operate("renew", fence(old), "host"), null);
  setupState.advance(LEASE_MS - 1);
  assert.equal(await restarted.operate("takeover", fence(old), "client"), null);
  setupState.advance(1);
  assert.equal((await restarted.operate("takeover", fence(old), "client")).epoch, 2);
});

test("private sessions cannot share a ledger or affect another session's fence", async () => {
  const a = setup().ledger;
  const b = setup().ledger;
  const first = await a.operate("create", initial, "a");
  const other = { session: "B".repeat(64), epoch: 0, token: "" };
  const second = await b.operate("create", other, "b");
  assert.equal(await a.operate("read", other, "b"), null);
  assert.equal(await a.operate("release", { ...fence(first), session: other.session }, "a"), null);
  assert.equal(await b.operate("renew", { ...fence(first), session: other.session }, "a"), null);
  assert.deepEqual(await b.operate("read", other, "b"), second);
});

test("malformed fences and unsafe numeric epochs fail closed", async () => {
  const { ledger } = setup();
  for (const request of [null, {}, { ...initial, holder: "host" }, { ...initial, epoch: -1 }, { ...initial, epoch: 2 ** 53 }, { ...initial, session: "public" }, { ...initial, token: null }]) {
    assert.equal(await ledger.operate("create", request, "host"), null);
  }
});

test("incompatible persistent state cannot create a replacement authority", async () => {
  const { ledger, storage } = setup();
  await storage.put("lease", { session, holder: "host", epoch: 1, token: "f".repeat(32) });
  await assert.rejects(ledger.restart(), /Invalid lease state/);
  await assert.rejects(ledger.operate("create", initial, "client"), /Invalid lease state/);
  await assert.rejects(ledger.operate("takeover", { session, epoch: 1, token: "f".repeat(32) }, "client"), /Invalid lease state/);
});
