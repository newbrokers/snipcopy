import type { Metadata } from "next";
import Link from "next/link";
import "./globals.css";

export const metadata: Metadata = {
  title: "SavedCode - Practical Software Tools",
  description: "SavedCode builds focused desktop tools with offline-friendly yearly licenses for SnipCopy, Draw Overlay, and Audio Crop.",
  icons: {
    icon: [{ url: "/icon.png", type: "image/png" }],
    apple: [{ url: "/icon.png", type: "image/png" }]
  }
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en">
      <body>
        <div className="site-shell">
          <header className="nav">
            <div className="nav-inner">
              <Link className="brand" href="/">
                <span className="brand-mark">SC</span>
                <span>SavedCode</span>
              </Link>
              <nav className="nav-links" aria-label="Main navigation">
                <Link href="/pricing">Pricing</Link>
                <Link href="/download">Download</Link>
                <Link href="/activate">Activation</Link>
                <Link href="/portal">Portal</Link>
                <Link href="/admin">Admin</Link>
              </nav>
              <Link className="button primary" href="/pricing">
                Pro Licenses
              </Link>
            </div>
          </header>
          {children}
          <footer className="footer">
            <div className="section">
              <span>SavedCode software tools</span>
              <span style={{ marginLeft: 16 }}>
                <Link href="/privacy">Privacy</Link> / <Link href="/terms">Terms</Link>
              </span>
            </div>
          </footer>
        </div>
      </body>
    </html>
  );
}
