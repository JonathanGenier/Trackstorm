import test from "node:test";
import assert from "node:assert/strict";
import { Miniflare, convertV4MiniflareOptions } from "miniflare";
import { generateKeyPair, exportJWK, SignJWT, createLocalJWKSet } from "jose";
import { authenticate, CONNECT_JWKS } from "../src/eos-identity.js";

const env = { EOS_CLIENT_ID: "test-client", EOS_PRODUCT_ID: "test-product", EOS_SANDBOX_ID: "test-sandbox", EOS_DEPLOYMENT_ID: "test-deployment" };
const key = await generateKeyPair("RS256");
const jwks = { keys: [{ ...await exportJWK(key.publicKey), kid: "test", alg: "RS256", use: "sig" }] };
const now = Math.floor(Date.now() / 1000);
const claims = { iss: "https://api.epicgames.dev/auth/v1", aud: env.EOS_CLIENT_ID, sub: "host", iat: now - 10, exp: now + 3600, pfpid: env.EOS_PRODUCT_ID, pfsid: env.EOS_SANDBOX_ID, pfdid: env.EOS_DEPLOYMENT_ID };
const sign = (changes = {}, signingKey = key.privateKey, header = { alg: "RS256", kid: "test" }) => new SignJWT({ ...claims, ...changes }).setProtectedHeader(header).sign(signingKey);

test("Connect identity validation checks verified claims, origin, signature and lifetime", async () => {
  const keys = createLocalJWKSet(jwks);
  const clock = new Date(now * 1000);
  assert.equal(await authenticate("Bearer " + await sign(), env, keys, clock), "host");
  for (const changes of [
    { iss: "https://api.epicgames.dev.evil.test" }, { iss: "http://api.epicgames.dev" },
    { aud: "other-client" }, { sub: "" }, { sub: "raw identity" }, { iat: now + 1 }, { iat: undefined },
    { exp: now }, { exp: undefined }, { pfpid: "other-product" }, { pfsid: "other-sandbox" }, { pfdid: "other-deployment" },
  ]) await assert.rejects(authenticate("Bearer " + await sign(changes), env, keys, clock));
  const other = await generateKeyPair("RS256");
  await assert.rejects(authenticate("Bearer " + await sign({}, other.privateKey), env, keys, clock));
  await assert.rejects(authenticate("Bearer " + await sign({}, key.privateKey, { alg: "RS256" }), env, keys, clock));
  await assert.rejects(authenticate("Bearer " + await sign({}, key.privateKey, { alg: "RS256", kid: "unknown" }), env, keys, clock));
  const unsigned = Buffer.from(JSON.stringify({ alg: "none", kid: "test" })).toString("base64url") + "." + Buffer.from(JSON.stringify(claims)).toString("base64url") + ".";
  await assert.rejects(authenticate("Bearer " + unsigned, env, keys, clock));
  await assert.rejects(authenticate("Bearer " + await sign(), {}, keys, clock));
  await assert.rejects(authenticate("host", env, keys, clock));
});

test("actual Worker HTTP and SQLite Durable Objects isolate sessions and serialize competing takeovers", async () => {
  const runtime = new Miniflare(convertV4MiniflareOptions({
    modules: true, scriptPath: ".wrangler/build/worker.js", compatibilityDate: "2026-09-17",
    bindings: env, durableObjects: { LEASE_SESSIONS: { className: "LeaseSession", useSQLite: true } },
    outboundService: request => {
      assert.equal(request.url, CONNECT_JWKS);
      return Response.json(jwks);
    },
  }));
  try {
    const session = "A".repeat(64);
    const initial = { session, epoch: 0, token: "" };
    const token = await sign();
    const send = (operation, body, bearer = token) => runtime.dispatchFetch("https://leases.test/lease/" + operation, {
      method: "POST", headers: { "Content-Type": "application/json", ...(bearer ? { Authorization: "Bearer " + bearer } : {}) }, body: JSON.stringify(body),
    });
    const fence = grant => ({ session: grant.session, epoch: grant.epoch, token: grant.token });
    assert.equal((await send("create", initial, null)).status, 401);
    assert.equal((await send("create", { ...initial, holder: "spoofed" })).status, 400);
    const created = await send("create", initial);
    assert.equal(created.status, 200);
    assert.equal(created.headers.get("Cache-Control"), "no-store");
    const first = await created.json();
    assert.deepEqual(Object.keys(first).sort(), ["epoch", "holder", "remainingSeconds", "session", "token"]);
    assert.equal(first.holder, "host");
    assert.equal(first.epoch, 1);
    assert.equal((await send("create", initial)).status, 409);
    const renewed = await (await send("renew", fence(first))).json();
    assert.notEqual(renewed.token, first.token);
    assert.equal((await send("release", fence(first))).status, 409);

    const secondSession = { session: "B".repeat(64), epoch: 0, token: "" };
    assert.equal((await send("create", secondSession)).status, 200);
    assert.equal((await send("release", { ...fence(renewed), session: secondSession.session })).status, 409);
    assert.equal((await send("takeover", fence(renewed), await sign({ sub: "client" }))).status, 409);
    assert.equal((await (await send("release", fence(renewed))).json()).remainingSeconds, 0);
    const credentials = await Promise.all(Array.from({ length: 16 }, (_, i) => sign({ sub: `client-${i}` })));
    const attempts = await Promise.all(credentials.map(identity => send("takeover", fence(renewed), identity)));
    assert.equal(attempts.filter(response => response.status === 200).length, 1);
    assert.equal(attempts.filter(response => response.status === 409).length, 15);
    const winner = await attempts.find(response => response.status === 200).json();
    assert.equal(winner.epoch, 2);
    for (const operation of ["renew", "release", "takeover"]) assert.equal((await send(operation, fence(renewed))).status, 409);
    const electedIdentity = await sign({ sub: winner.holder });
    assert.equal((await send("release", fence(winner), electedIdentity)).status, 200);
    const third = await (await send("takeover", fence(winner))).json();
    assert.equal(third.epoch, 3);
    assert.equal(third.holder, "host");
    assert.equal((await send("renew", fence(winner), electedIdentity)).status, 409);
    assert.equal((await (await send("read", secondSession)).json()).epoch, 1);
    assert.equal((await send("create", { ...initial, session: "C".repeat(64) }, await sign({ pfdid: "wrong" }))).status, 401);
    assert.equal((await send("create", { ...initial, token: "x".repeat(3000) })).status, 400);
  } finally { await runtime.dispose(); }
});

test("unreachable coordination binding returns failure without a grant", async () => {
  const runtime = new Miniflare(convertV4MiniflareOptions({
    modules: true, scriptPath: ".wrangler/build/worker.js", compatibilityDate: "2026-09-17", bindings: env,
    outboundService: () => Response.json(jwks),
  }));
  try {
    const response = await runtime.dispatchFetch("https://leases.test/lease/create", {
      method: "POST", headers: { "Content-Type": "application/json", Authorization: "Bearer " + await sign() },
      body: JSON.stringify({ session: "A".repeat(64), epoch: 0, token: "" }),
    });
    assert.equal(response.status, 503);
    assert.deepEqual(await response.json(), { error: "Coordination unavailable." });
  } finally { await runtime.dispose(); }
});

test("unconfigured Worker fails closed and never creates an unauthenticated object", async () => {
  const runtime = new Miniflare(convertV4MiniflareOptions({ modules: true, scriptPath: ".wrangler/build/worker.js", compatibilityDate: "2026-09-17", durableObjects: { LEASE_SESSIONS: { className: "LeaseSession", useSQLite: true } } }));
  try {
    assert.equal((await runtime.dispatchFetch("https://leases.test/health")).status, 200);
    assert.match(await (await runtime.dispatchFetch("https://leases.test/licenses")).text(), /Copyright \(c\) 2018 Filip Skokan/);
    assert.equal((await runtime.dispatchFetch("https://leases.test/lease/create", { method: "POST", body: "{}" })).status, 503);
    assert.equal((await runtime.dispatchFetch("http://leases.test/lease/create", { method: "POST", body: "{}" })).status, 400);
  } finally { await runtime.dispose(); }
});
