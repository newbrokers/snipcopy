import Link from "next/link";
import { ProductMockup } from "@/components/ProductMockup";
import { PRODUCT_LIST } from "@/lib/products";

const features = [
  ["Focused utilities", "Small software products built around practical workflows instead of bloated suites."],
  ["Offline-friendly licenses", "Yearly Pro licenses use signed tokens so apps can keep working without constant server checks."],
  ["Shared billing", "One SavedCode backend handles Stripe checkout, renewals, and license sync for every product."]
];

export default function Home() {
  return (
    <main>
      <section className="hero">
        <div className="section hero-grid">
          <div>
            <div className="eyebrow">Software tools that stay useful</div>
            <h1>SavedCode</h1>
            <p className="lead">
              A home for focused desktop apps with simple pricing, Stripe billing, and offline-friendly licenses. Starting
              with SnipCopy, Draw Overlay, and Audio Crop.
            </p>
            <div className="actions">
              <Link className="button primary" href="/download">
                Download Tools
              </Link>
              <Link className="button mint" href="/pricing">
                View Pricing
              </Link>
            </div>
          </div>
          <ProductMockup />
        </div>
      </section>

      <section className="feature-band">
        <div className="section">
          <h2>One license system for many useful products.</h2>
          <div className="product-grid">
            {PRODUCT_LIST.map((product) => (
              <article className="card product-card" key={product.slug}>
                <div className="product-heading">
                  <img className="product-icon" src={product.imageSrc} alt={`${product.name} icon`} />
                  <div>
                    <span className="product-pill">{product.slug}</span>
                    <h3>{product.name}</h3>
                  </div>
                </div>
                <p>{product.description}</p>
              </article>
            ))}
          </div>
          <div className="grid">
            {features.map(([title, text]) => (
              <article className="card" key={title}>
                <h3>{title}</h3>
                <p>{text}</p>
              </article>
            ))}
          </div>
        </div>
      </section>
    </main>
  );
}
