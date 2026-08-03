"use client";

import { useState } from "react";

const products = [
  { slug: "", name: "All products" },
  { slug: "snipcopy", name: "SnipCopy" },
  { slug: "draw-overlay", name: "Draw Overlay" },
  { slug: "audio-crop", name: "Audio Crop" }
];

export function PortalTools() {
  const [email, setEmail] = useState("");
  const [licenseKey, setLicenseKey] = useState("");
  const [productSlug, setProductSlug] = useState("");
  const [result, setResult] = useState<string>("");

  async function loadStatus() {
    const params = new URLSearchParams();
    if (email) params.set("email", email);
    if (licenseKey) params.set("licenseKey", licenseKey);
    if (productSlug) params.set("product_slug", productSlug);
    const response = await fetch(`/api/license/status?${params.toString()}`);
    const data = await response.json();
    setResult(JSON.stringify(data, null, 2));
  }

  async function openBillingPortal() {
    const response = await fetch("/api/billing/portal", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ email })
    });
    const data = await response.json();
    if (response.ok) window.location.href = data.url;
    else setResult(JSON.stringify(data, null, 2));
  }

  return (
    <div className="form">
      <input className="input" type="email" placeholder="Purchase email" value={email} onChange={(event) => setEmail(event.target.value)} />
      <input className="input" placeholder="License key" value={licenseKey} onChange={(event) => setLicenseKey(event.target.value)} />
      <select className="input" value={productSlug} onChange={(event) => setProductSlug(event.target.value)} aria-label="Product">
        {products.map((product) => (
          <option key={product.slug || "all"} value={product.slug}>
            {product.name}
          </option>
        ))}
      </select>
      <div className="actions" style={{ marginTop: 0 }}>
        <button className="button" type="button" onClick={loadStatus}>
          Check status
        </button>
        <button className="button primary" type="button" onClick={openBillingPortal}>
          Manage billing
        </button>
      </div>
      {result ? <pre className="card" style={{ overflow: "auto" }}>{result}</pre> : null}
    </div>
  );
}
