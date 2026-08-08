"use client";

import { useState } from "react";

type CheckoutButtonProps = {
  productSlug?: string;
  productName?: string;
};

export function CheckoutButton({ productSlug = "snipcopy", productName = "SnipCopy" }: CheckoutButtonProps) {
  const [email, setEmail] = useState("");
  const [message, setMessage] = useState("");
  const [loading, setLoading] = useState(false);
  const normalizedEmail = email.trim().toLowerCase();
  const emailIsValid = /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(normalizedEmail);

  async function startCheckout() {
    if (!emailIsValid) {
      setMessage("Enter a valid email address first.");
      return;
    }

    setLoading(true);
    setMessage("");
    const response = await fetch("/api/checkout/create", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ email: normalizedEmail, product_slug: productSlug })
    });
    const data = await response.json();
    setLoading(false);
    if (!response.ok) {
      setMessage(data.error ?? "Checkout could not be started.");
      return;
    }
    window.location.href = data.url;
  }

  return (
    <div className="form">
      <input className="input" type="email" placeholder="Email for license delivery" value={email} onChange={(event) => setEmail(event.target.value)} />
      <button className="button primary" type="button" onClick={startCheckout} disabled={loading || !emailIsValid}>
        {loading ? "Opening Stripe..." : emailIsValid ? `Buy ${productName} Pro yearly` : "Enter email to buy"}
      </button>
      {message ? <p style={{ color: "#b34235", margin: 0 }}>{message}</p> : null}
    </div>
  );
}
