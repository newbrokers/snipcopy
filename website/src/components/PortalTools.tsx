"use client";

import { useEffect, useRef, useState } from "react";

const products = [
  { slug: "", name: "All products" },
  { slug: "snipcopy", name: "SnipCopy" },
  { slug: "draw-overlay", name: "Draw Overlay" },
  { slug: "audio-crop", name: "Audio Crop" }
];

type LicenseRecord = {
  licenseKey: string;
  productSlug: string;
  customerEmail: string;
  plan: string;
  status: string;
  expiresAt: string;
};

type ApiResult = {
  authenticated?: boolean;
  email?: string;
  error?: string;
  message?: string;
  licenses?: LicenseRecord[];
  devCode?: string;
};

async function readApiResult(response: Response): Promise<ApiResult> {
  const text = await response.text();
  if (!text) {
    return { error: response.ok ? "Empty server response." : "SavedCode could not complete that request. Check the server configuration and try again." };
  }

  try {
    return JSON.parse(text) as ApiResult;
  } catch {
    return { error: response.ok ? "Unexpected server response." : text.slice(0, 240) || "Unexpected server error." };
  }
}

export function PortalTools() {
  const emailRef = useRef<HTMLInputElement>(null);
  const codeRef = useRef<HTMLInputElement>(null);
  const licenseKeyRef = useRef<HTMLInputElement>(null);
  const productSlugRef = useRef<HTMLSelectElement>(null);
  const [email, setEmail] = useState("");
  const [code, setCode] = useState("");
  const [licenseKey, setLicenseKey] = useState("");
  const [productSlug, setProductSlug] = useState("");
  const [sessionEmail, setSessionEmail] = useState("");
  const [authenticated, setAuthenticated] = useState(false);
  const [sessionLoading, setSessionLoading] = useState(true);
  const [codeRequested, setCodeRequested] = useState(false);
  const [loading, setLoading] = useState(false);
  const [result, setResult] = useState<ApiResult | null>(null);
  const [copiedValue, setCopiedValue] = useState("");

  function setError(error: string) {
    setResult({ error });
  }

  async function refreshSession() {
    const response = await fetch("/api/portal/session");
    const data = await readApiResult(response);
    setAuthenticated(Boolean(data.authenticated));
    setSessionEmail(data.email ?? "");
    setSessionLoading(false);
    if (data.authenticated) await loadStatus();
  }

  async function requestCode() {
    const nextEmail = (emailRef.current?.value ?? email).trim();
    if (!nextEmail) {
      setError("Enter the email used for purchase.");
      return;
    }

    setEmail(nextEmail);
    setLoading(true);
    try {
      const params = new URLSearchParams(window.location.search);
      const response = await fetch("/api/portal/login/request", {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ email: nextEmail, session_id: params.get("session_id") ?? undefined })
      });
      const data = await readApiResult(response);
      setResult(data);
      if (response.ok) setCodeRequested(true);
    } finally {
      setLoading(false);
    }
  }

  async function verifyCode() {
    const nextEmail = (emailRef.current?.value ?? email).trim();
    const nextCode = (codeRef.current?.value ?? code).trim();
    if (!nextEmail || !nextCode) {
      setError("Enter your email and the 6-digit code.");
      return;
    }

    setLoading(true);
    try {
      const response = await fetch("/api/portal/login/verify", {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ email: nextEmail, code: nextCode })
      });
      const data = await readApiResult(response);
      setResult(data);
      if (response.ok && data.authenticated) {
        setAuthenticated(true);
        setSessionEmail(data.email ?? nextEmail);
        setCode("");
        await loadStatus();
      }
    } finally {
      setLoading(false);
    }
  }

  async function loadStatus() {
    const nextLicenseKey = (licenseKeyRef.current?.value ?? licenseKey).trim();
    const nextProductSlug = productSlugRef.current?.value ?? productSlug;
    const params = new URLSearchParams();
    if (nextLicenseKey) params.set("licenseKey", nextLicenseKey);
    if (nextProductSlug) params.set("product_slug", nextProductSlug);
    setLicenseKey(nextLicenseKey);
    setProductSlug(nextProductSlug);
    setLoading(true);
    try {
      const response = await fetch(`/api/license/status?${params.toString()}`);
      const data = await readApiResult(response);
      setResult(data);
    } finally {
      setLoading(false);
    }
  }

  async function openBillingPortal() {
    const portalWindow = window.open("about:blank", "_blank");
    if (portalWindow) portalWindow.opener = null;

    setLoading(true);
    try {
      const response = await fetch("/api/billing/portal", { method: "POST" });
      const data = (await readApiResult(response)) as ApiResult & { url?: string };
      if (response.ok && data.url) {
        if (portalWindow) portalWindow.location.href = data.url;
        else window.location.href = data.url;
      } else {
        if (portalWindow) portalWindow.close();
        setResult(data);
      }
    } finally {
      setLoading(false);
    }
  }

  async function logout() {
    await fetch("/api/portal/logout", { method: "POST" });
    setAuthenticated(false);
    setSessionEmail("");
    setCodeRequested(false);
    setLicenseKey("");
    setResult(null);
  }

  async function copyValue(value: string) {
    await navigator.clipboard.writeText(value);
    setCopiedValue(value);
    window.setTimeout(() => setCopiedValue((current) => (current === value ? "" : current)), 2000);
  }

  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    if (params.get("checkout") === "success") {
      setResult({ message: "Payment complete. Sign in with your purchase email to view your license key." });
    }
    void refreshSession();
  }, []);

  const licenses = result?.licenses ?? [];

  if (sessionLoading) {
    return <div className="notice">Checking portal session...</div>;
  }

  if (!authenticated) {
    return (
      <div className="form">
        <input ref={emailRef} className="input" type="email" placeholder="Purchase email" value={email} onChange={(event) => setEmail(event.target.value)} />
        {codeRequested ? (
          <input
            ref={codeRef}
            className="input"
            inputMode="numeric"
            maxLength={6}
            placeholder="6-digit code"
            value={code}
            onChange={(event) => setCode(event.target.value.replace(/\D/g, "").slice(0, 6))}
          />
        ) : null}
        <div className="actions" style={{ marginTop: 0 }}>
          <button className="button" type="button" onClick={requestCode} disabled={loading}>
            {codeRequested ? "Send new code" : "Email sign-in code"}
          </button>
          {codeRequested ? (
            <button className="button primary" type="button" onClick={verifyCode} disabled={loading}>
              Sign in
            </button>
          ) : null}
        </div>
        {result?.error ? <div className="notice error">{result.error}</div> : null}
        {result?.message ? <div className="notice">{result.message}</div> : null}
        {result?.devCode ? <div className="notice">Development code: {result.devCode}</div> : null}
      </div>
    );
  }

  return (
    <div className="form">
      <div className="session-bar">
        <div>
          <span>Signed in as</span>
          <strong>{sessionEmail}</strong>
        </div>
        <button className="button" type="button" onClick={logout}>
          Sign out
        </button>
      </div>
      <input ref={licenseKeyRef} className="input" placeholder="Filter by license key" value={licenseKey} onChange={(event) => setLicenseKey(event.target.value)} />
      <select ref={productSlugRef} className="input" value={productSlug} onChange={(event) => setProductSlug(event.target.value)} aria-label="Product">
        {products.map((product) => (
          <option key={product.slug || "all"} value={product.slug}>
            {product.name}
          </option>
        ))}
      </select>
      <div className="actions" style={{ marginTop: 0 }}>
        <button className="button" type="button" onClick={() => loadStatus()} disabled={loading}>
          {loading ? "Checking..." : "Refresh licenses"}
        </button>
        <button className="button primary" type="button" onClick={openBillingPortal} disabled={loading}>
          Manage subscription
        </button>
      </div>
      {result?.error ? <div className="notice error">{result.error}</div> : null}
      {result && !result.error && !licenses.length ? <div className="notice">No licenses were found for this account yet.</div> : null}
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
              <label className="license-copy-row">
                <span>Purchase email</span>
                <input className="input" readOnly value={license.customerEmail} />
                <button className="button copy-button" type="button" onClick={() => copyValue(license.customerEmail)}>
                  {copiedValue === license.customerEmail ? "✓" : "Copy"}
                </button>
              </label>
              <label className="license-copy-row">
                <span>License key</span>
                <input className="input" readOnly value={license.licenseKey} />
                <button className="button copy-button" type="button" onClick={() => copyValue(license.licenseKey)}>
                  {copiedValue === license.licenseKey ? "✓" : "Copy"}
                </button>
              </label>
            </article>
          ))}
        </div>
      ) : null}
    </div>
  );
}
