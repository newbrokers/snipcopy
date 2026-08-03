export default function ActivatePage() {
  return (
    <main className="page">
      <section className="section">
        <div className="eyebrow">Activation help</div>
        <h1>Activate or sync your SavedCode license.</h1>
        <p className="lead">
          After purchase, each SavedCode product receives a license key and signed token. SnipCopy, Draw Overlay, and Audio
          Crop all use the same offline-friendly license flow.
        </p>
        <div className="grid">
          <article className="card">
            <h3>Activate</h3>
            <p>Open the product, choose Pro activation, then paste your license key and purchase email. The first successful activation locks the license to that device.</p>
          </article>
          <article className="card">
            <h3>Offline use</h3>
            <p>The app verifies the token signature locally with the public key. No private signing key ships in the app.</p>
          </article>
          <article className="card">
            <h3>Renewal sync</h3>
            <p>When a yearly subscription renews, sync from the activated app to receive a token with the updated expiry.</p>
          </article>
        </div>
      </section>
    </main>
  );
}
