"use client";

import { useState } from "react";

export function AdminSearch() {
  const [token, setToken] = useState("");
  const [query, setQuery] = useState("");
  const [result, setResult] = useState("");

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

  return (
    <div className="form">
      <input className="input" type="password" placeholder="Admin token" value={token} onChange={(event) => setToken(event.target.value)} />
      <input className="input" placeholder="Search email, license, product, Stripe ID" value={query} onChange={(event) => setQuery(event.target.value)} />
      <button className="button primary" type="button" onClick={search}>
        Search licenses
      </button>
      {result ? <pre className="card" style={{ overflow: "auto" }}>{result}</pre> : null}
    </div>
  );
}
