import { generateKeyPairSync } from "crypto";

const { privateKey, publicKey } = generateKeyPairSync("ed25519", {
  privateKeyEncoding: { type: "pkcs8", format: "pem" },
  publicKeyEncoding: { type: "spki", format: "pem" }
});

console.log("LICENSE_PRIVATE_KEY_PEM_BASE64=" + Buffer.from(privateKey).toString("base64"));
console.log("LICENSE_PUBLIC_KEY_PEM_BASE64=" + Buffer.from(publicKey).toString("base64"));
