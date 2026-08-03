export default function PrivacyPage() {
  return (
    <main className="page">
      <section className="section legal">
        <div className="eyebrow">Privacy</div>
        <h1>Privacy policy</h1>
        <p>
          SavedCode builds focused software products and license services. SnipCopy is designed to keep screenshots local.
          This website stores only the information needed to process purchases, issue licenses, provide support, and secure
          the service.
        </p>
        <h2>Data we process</h2>
        <p>Purchase email, Stripe customer/subscription identifiers, license keys, activation timestamps, and limited request metadata for abuse prevention.</p>
        <h2>Payments</h2>
        <p>Payments are processed by Stripe. SavedCode does not store card numbers.</p>
        <h2>Screenshots</h2>
        <p>The desktop app should not upload screenshot contents for licensing. Any future cloud feature should require explicit opt-in.</p>
      </section>
    </main>
  );
}
