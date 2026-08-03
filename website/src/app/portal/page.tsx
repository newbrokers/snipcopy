import { PortalTools } from "@/components/PortalTools";

export default function PortalPage() {
  return (
    <main className="page">
      <section className="section two-col">
        <div>
          <div className="eyebrow">Customer portal</div>
          <h1>Find your SavedCode license and manage billing.</h1>
          <p className="lead">
            Use the email from checkout to check license metadata for SavedCode products or open Stripe&apos;s secure billing
            portal.
          </p>
        </div>
        <PortalTools />
      </section>
    </main>
  );
}
