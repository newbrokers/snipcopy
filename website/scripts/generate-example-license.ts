import { generateKeyPairSync } from "crypto";
import { createPayload, generateLicenseKey, signLicensePayload, verifyLicenseToken } from "../src/lib/license";

const { privateKey, publicKey } = generateKeyPairSync("ed25519", {
  privateKeyEncoding: { type: "pkcs8", format: "pem" },
  publicKeyEncoding: { type: "spki", format: "pem" }
});

const privateKeyBase64 = Buffer.from(privateKey).toString("base64");
const publicKeyBase64 = Buffer.from(publicKey).toString("base64");
const expiresAt = new Date();
expiresAt.setUTCFullYear(expiresAt.getUTCFullYear() + 1);

const payload = createPayload({
  licenseKey: generateLicenseKey(),
  customerEmail: "customer@example.com",
  expiresAt
});
const token = signLicensePayload(payload, privateKeyBase64);

console.log(JSON.stringify({ payload, token, verified: verifyLicenseToken(token, publicKeyBase64) }, null, 2));
