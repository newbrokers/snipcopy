export default function TermsPage() {
  return (
    <main className="page">
      <section className="section legal">
        <div className="eyebrow">Terms</div>
        <h1>Terms of service</h1>
        <p>
          SavedCode products may include free and paid editions. SnipCopy Free may be used without payment. SnipCopy Pro is
          licensed yearly and includes the Pro features listed on the pricing page while the license token remains valid.
        </p>
        <h2>License</h2>
        <p>A Pro license is tied to the purchasing customer email and may be activated on one device at a time. Contact support if you need to move it to a replacement device.</p>
        <h2>Renewals and cancellation</h2>
        <p>Subscriptions renew yearly through Stripe. If canceled, the current token remains valid until its paid-through expiry.</p>
        <h2>Support</h2>
        <p>Priority support is provided for active Pro customers using the purchase email.</p>
      </section>
    </main>
  );
}
