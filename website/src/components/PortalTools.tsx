"use client";

import { useEffect, useRef, useState } from "react";

const products = [
  { slug: "", name: "All products" },
  { slug: "snipcopy", name: "SnipCopy" },
  { slug: "draw-overlay", name: "Draw Overlay" },
  { slug: "audio-crop", name: "Audio Crop" }
];

export function PortalTools() {
  const emailRef = useRef<HTMLInputElement>(null);
  const licenseKeyRef = useRef<HTMLInputElement>(null);
  const productSlugRef = useRef<HTMLSelectElement>(null);
  const [email, setEmail] = useState("");
  const [licenseKey, setLicenseKey] = useState("");
  const [productSlug, setProductSlug] = useState("");
  const [result, setResult] = useState<Record<string, unknown> | null>(null);
  const [loading, setLoading] = useState(false);

  function readFormValues() {
    const nextEmail = (emailRef.current?.value ?? email).trim();
    const nextLicenseKey = (licenseKeyRef.current?.value ?? licenseKey).trim();
    const nextProductSlug = productSlugRef.current?.value ?? productSlug;
    setEmail(nextEmail);
    setLicenseKey(nextLicenseKey);
    setProductSlug(nextProductSlug);
    return { nextEmail, nextLicenseKey, nextProductSlug };
  }

  async function loadStatus(sessionId?: string) {
    const { nextEmail, nextLicenseKey, nextProductSlug } = readFormValues();
    const params = new URLSearchParams();
    if (nextEmail) params.set("email", nextEmail);
    if (nextLicenseKey) params.set("licenseKey", nextLicenseKey);
    if (nextProductSlug) params.set("product_slug", nextProductSlug);
    if (sessionId) params.set("session_id", sessionId);
    setLoading(true);
    try {
      const response = await fetch(`/api/license/status?${params.toString()}`);
      const data = await response.json();
      setResult(data);
    } finally {
      setLoading(false);
    }
  }

  async function openBillingPortal() {
    const { nextEmail } = readFormValues();
    const response = await fetch("/api/billing/portal", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ email: nextEmail })
    });
    const data = await response.json();
    if (response.ok) window.location.href = data.url;
    else setResult(data);
  }

  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    const sessionId = params.get("session_id");
    if (params.get("checkout") === "success" && sessionId) {
      void loadStatus(sessionId);
    }
  }, []);

  const licenses = Array.isArray(result?.licenses) ? (result.licenses as Array<Record<string, string>>) : [];

  return (
    <div className="form">
      <input ref={emailRef} className="input" type="email" placeholder="Purchase email" value={email} onChange={(event) => setEmail(event.target.value)} />
      <input ref={licenseKeyRef} className="input" placeholder="License key" value={licenseKey} onChange={(event) => setLicenseKey(event.target.value)} />
      <select ref={productSlugRef} className="input" value={productSlug} onChange={(event) => setProductSlug(event.target.value)} aria-label="Product">
        {products.map((product) => (
          <option key={product.slug || "all"} value={product.slug}>
            {product.name}
          </option>
        ))}
      </select>
      <div className="actions" style={{ marginTop: 0 }}>
        <button className="button" type="button" onClick={() => loadStatus()} disabled={loading}>
          {loading ? "Checking..." : "Check status"}
        </button>
        <button className="button primary" type="button" onClick={openBillingPortal} disabled={loading}>
          Manage billing
        </button>
      </div>
      {result?.error ? <div className="notice error">{String(result.error)}</div> : null}
      {result?.message ? <div className="notice">{String(result.message)}</div> : null}
      {result && !result.error && !licenses.length && !result.message ? <div className="notice">No license was found for those details yet.</div> : null}
      {licenses.length ? (
        <div className="license-results">
          {licenses.map((license) => (
            <article className="license-record" key={license.licenseKey}>
              <div>
                <span className="product-pill">{license.productSlug}</span>
                <h3>{license.plan} license</h3>
              </div>
              <div className="license-meta">
                <span>Status: {license.status}</span>
                <span>Expires: {new Date(license.expiresAt).toLocaleDateString()}</span>
              </div>
              <label className="license-key-row">
                <span>License key</span>
                <input className="input" readOnly value={license.licenseKey} />
                <button className="button" type="button" onClick={() => navigator.clipboard.writeText(license.licenseKey)}>
                  Copy
                </button>
              </label>
            </article>
          ))}
        </div>
      ) : null}
    </div>
  );
}
