"use client";

import { useState } from "react";
import { PRODUCT_LIST } from "@/lib/products";

type ManualLicenseResult = {
  license?: {
    licenseKey: string;
    customerEmail: string;
    productSlug: string;
    plan: string;
    status: string;
    issuedAt: string;
    expiresAt: string;
    activationEmail: string;
  };
  error?: string;
};

export function AdminSearch() {
  const [token, setToken] = useState("");
  const [query, setQuery] = useState("");
  const [result, setResult] = useState("");
  const [manualEmail, setManualEmail] = useState("");
  const [manualProductSlug, setManualProductSlug] = useState("snipcopy");
  const [manualResult, setManualResult] = useState<ManualLicenseResult | null>(null);
  const [copiedValue, setCopiedValue] = useState("");

  async function readJson(response: Response) {
    const text = await response.text();
    if (!text) return { error: response.ok ? "Empty server response." : "SavedCode could not complete that request." };

    try {
      return JSON.parse(text);
    } catch {
      return { error: text.slice(0, 240) || "Unexpected server response." };
    }
  }

  async function search() {
    const params = query ? `?q=${encodeURIComponent(query)}` : "";
    const response = await fetch(`/api/admin/licenses${params}`, {
      headers: { authorization: `Bearer ${token}` }
    });
    setResult(JSON.stringify(await readJson(response), null, 2));
  }

  async function issueManualLicense() {
    setManualResult(null);
    const response = await fetch("/api/admin/licenses", {
      method: "POST",
      headers: {
        authorization: `Bearer ${token}`,
        "content-type": "application/json"
      },
      body: JSON.stringify({
        email: manualEmail,
        productSlug: manualProductSlug
      })
    });
    setManualResult((await readJson(response)) as ManualLicenseResult);
  }

  async function copyValue(value: string) {
    await navigator.clipboard.writeText(value);
    setCopiedValue(value);
    window.setTimeout(() => setCopiedValue((current) => (current === value ? "" : current)), 2000);
  }

  return (
    <div className="form">
      <input className="input" type="password" placeholder="Admin token" value={token} onChange={(event) => setToken(event.target.value)} />
      <div className="license-record">
        <div>
          <span className="product-pill">Manual</span>
          <h3>Generate one-year Pro license</h3>
        </div>
        <input className="input" type="email" placeholder="Friend email" value={manualEmail} onChange={(event) => setManualEmail(event.target.value)} />
        <select className="input" value={manualProductSlug} onChange={(event) => setManualProductSlug(event.target.value)}>
          {PRODUCT_LIST.map((product) => (
            <option key={product.slug} value={product.slug}>
              {product.name}
            </option>
          ))}
        </select>
        <button className="button primary" type="button" onClick={issueManualLicense}>
          Generate license
        </button>
        {manualResult?.error ? <div className="notice error">{manualResult.error}</div> : null}
        {manualResult?.license ? (
          <div className="license-results">
            <div className="license-meta">
              <span>Status: {manualResult.license.status}</span>
              <span>Expires: {new Date(manualResult.license.expiresAt).toLocaleDateString()}</span>
            </div>
            <label className="license-copy-row">
              <span>Activation email</span>
              <input className="input" readOnly value={manualResult.license.activationEmail} />
              <button className="button copy-button" type="button" onClick={() => copyValue(manualResult.license?.activationEmail ?? "")}>
                {copiedValue === manualResult.license.activationEmail ? "✓" : "Copy"}
              </button>
            </label>
            <label className="license-copy-row">
              <span>License key</span>
              <input className="input" readOnly value={manualResult.license.licenseKey} />
              <button className="button copy-button" type="button" onClick={() => copyValue(manualResult.license?.licenseKey ?? "")}>
                {copiedValue === manualResult.license.licenseKey ? "✓" : "Copy"}
              </button>
            </label>
          </div>
        ) : null}
      </div>
      <input className="input" placeholder="Search email, license, product, Stripe ID" value={query} onChange={(event) => setQuery(event.target.value)} />
      <button className="button primary" type="button" onClick={search}>
        Search licenses
      </button>
      {result ? <pre className="card" style={{ overflow: "auto" }}>{result}</pre> : null}
    </div>
  );
}
