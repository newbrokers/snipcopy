import { PortalTools } from "@/components/PortalTools";

export default function PortalPage() {
  return (
    <main className="page">
      <section className="section two-col">
        <div>
          <div className="eyebrow">Customer portal</div>
          <h1>Find your SavedCode license and manage billing.</h1>
          <p className="lead">
            Sign in with the email from checkout to view your license keys, copy activation details, and open Stripe&apos;s
            secure billing portal.
          </p>
        </div>
        <PortalTools />
      </section>
    </main>
  );
}
