/*!
 * jose 6.2.12 — The MIT License (MIT)
 * Copyright (c) 2018 Filip Skokan
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 */
import { createRemoteJWKSet, jwtVerify } from "jose";

// Epic's Connect keys, not Epic Account Services Auth keys. Never read a URL from a token.
export const CONNECT_JWKS = "https://api.epicgames.dev/auth/v1/oauth/jwks";
const keys = createRemoteJWKSet(new URL(CONNECT_JWKS), { timeoutDuration: 1500, cacheMaxAge: 600_000, cooldownDuration: 30_000 });

export function configured(env) {
  return [env.EOS_CLIENT_ID, env.EOS_PRODUCT_ID, env.EOS_SANDBOX_ID, env.EOS_DEPLOYMENT_ID]
    .every(value => typeof value === "string" && value.length > 0 && value.length <= 64 && !/[\s<>]/.test(value));
}

export async function authenticate(authorization, env, keySource = keys, currentDate = new Date()) {
  if (!configured(env) || typeof authorization !== "string" || authorization.length > 8192 || !authorization.startsWith("Bearer ")) {
    throw new Error("Authentication required.");
  }

  const { payload, protectedHeader } = await jwtVerify(authorization.slice(7), keySource, {
    algorithms: ["RS256"], audience: env.EOS_CLIENT_ID, clockTolerance: 0, currentDate,
    requiredClaims: ["iss", "aud", "iat", "exp", "sub", "pfpid", "pfsid", "pfdid"],
  });
  // Epic documents an issuer with this base URL, not one fixed path. Parse the origin
  // rather than accepting an attacker-controlled prefix such as api.epicgames.dev.evil.
  const issuer = new URL(payload.iss);
  if (typeof protectedHeader.kid !== "string" || protectedHeader.kid.length === 0 ||
      issuer.origin !== "https://api.epicgames.dev" || issuer.username || issuer.password || issuer.search || issuer.hash ||
      !Number.isSafeInteger(payload.iat) || payload.iat > Math.floor(currentDate.getTime() / 1000) ||
      !Number.isSafeInteger(payload.exp) || payload.exp <= payload.iat ||
      typeof payload.sub !== "string" || payload.sub.length === 0 || payload.sub.length > 256 || /\s/.test(payload.sub) ||
      payload.pfpid !== env.EOS_PRODUCT_ID || payload.pfsid !== env.EOS_SANDBOX_ID || payload.pfdid !== env.EOS_DEPLOYMENT_ID) {
    throw new Error("Authentication rejected.");
  }
  return payload.sub;
}
