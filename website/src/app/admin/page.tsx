import { AdminSearch } from "@/components/AdminSearch";

export default function AdminPage() {
  return (
    <main className="page">
      <section className="section two-col">
        <div>
          <div className="eyebrow">Admin</div>
          <h1>SavedCode license dashboard.</h1>
          <p className="lead">
            Search product licenses, customer emails, Stripe IDs, token expiry, and recent activations with the server-side
            admin token.
          </p>
        </div>
        <AdminSearch />
      </section>
    </main>
  );
}
