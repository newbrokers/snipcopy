import { PRODUCT_LIST } from "@/lib/products";

export default function DownloadPage() {
  return (
    <main className="page">
      <section className="section">
        <div>
          <div className="eyebrow">SavedCode downloads</div>
          <h1>Install SavedCode tools for Windows.</h1>
          <p className="lead">
            Download the desktop utilities you license through SavedCode. Each Pro edition verifies its license offline and
            syncs after renewal.
          </p>
        </div>
        <div className="product-grid">
          {PRODUCT_LIST.map((product) => (
            <article className="card product-card" key={product.slug}>
              <div className="product-heading">
                <img className="product-icon" src={product.imageSrc} alt={`${product.name} icon`} />
                <div>
                  <span className="product-pill">{product.slug}</span>
                  <h2>{product.name}</h2>
                </div>
              </div>
              <p>{product.description}</p>
              <ul className="list">
                <li>Windows 10 or newer</li>
                <li>Offline Pro token verification</li>
                <li>Yearly SavedCode license support</li>
              </ul>
              <div className="actions">
                {product.downloadHref ? (
                  <a className="button primary" href={product.downloadHref}>
                    Download
                  </a>
                ) : (
                  <span className="button disabled" aria-disabled="true">
                    Coming soon
                  </span>
                )}
                <a className="button" href="/activate">
                  Activate Pro
                </a>
              </div>
            </article>
          ))}
        </div>
      </section>
    </main>
  );
}
