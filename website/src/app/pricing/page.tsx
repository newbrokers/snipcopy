import { CheckoutButton } from "@/components/CheckoutButton";
import { PRODUCT_LIST } from "@/lib/products";

export default function PricingPage() {
  return (
    <main className="page">
      <section className="section">
        <div className="eyebrow">SavedCode pricing</div>
        <h1>Choose the yearly Pro license for the tool you use.</h1>
        <div className="product-grid">
          {PRODUCT_LIST.map((product) => (
            <article className="card product-card" key={product.slug}>
              <div className="product-heading">
                <img className="product-icon" src={product.imageSrc} alt={`${product.name} icon`} />
                <span className="product-pill">{product.slug}</span>
              </div>
              <h2>{product.name} Pro</h2>
              <p>{product.headline}</p>
              <h3>Free</h3>
              <ul className="list">{product.freeFeatures.map((item) => <li key={item}>{item}</li>)}</ul>
              <h3>Pro yearly</h3>
              <ul className="list">{product.proFeatures.map((item) => <li key={item}>{item}</li>)}</ul>
              {product.downloadHref ? (
                <CheckoutButton productSlug={product.slug} productName={product.name} />
              ) : (
                <div className="notice">Coming soon. Pricing is prepared, but this app is not packaged yet.</div>
              )}
            </article>
          ))}
        </div>
        <div className="grid">
          <article className="card" style={{ gridColumn: "1 / -1" }}>
            <h2>License model</h2>
            <p>
              SavedCode products use offline-verifiable license tokens. Each app can validate its signed token with only the
              public key, then sync after renewal to receive the next yearly token. Each yearly Pro license includes one
              active activation per system; contact support to move it to a replacement system.
            </p>
          </article>
        </div>
      </section>
    </main>
  );
}
