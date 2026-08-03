export function ProductMockup() {
  return (
    <div className="mock-window" aria-label="SnipCopy editor mockup">
      <div className="mock-titlebar">
        <span className="dot" />
        <span className="dot" />
        <span className="dot" />
        <span style={{ marginLeft: 8 }}>SnipCopy Editor</span>
      </div>
      <div className="mock-canvas">
        <div className="snip-area">
          <div className="callout">1</div>
          <div className="arrow-line" />
          <h3>Drag, mark, copy.</h3>
          <p style={{ color: "#41516d", maxWidth: 300 }}>
            Capture an area, add clear markup, redact sensitive details, and send the final snip to the clipboard.
          </p>
          <div style={{ marginTop: 20, width: 180, height: 26, background: "#162033", borderRadius: 4 }} />
          <div style={{ marginTop: 10, width: 230, height: 18, background: "#dce3ee", borderRadius: 4 }} />
          <div style={{ marginTop: 8, width: 140, height: 18, background: "#dce3ee", borderRadius: 4 }} />
        </div>
      </div>
      <div className="toolbar">
        <span className="tool">P</span>
        <span className="tool">A</span>
        <span className="tool">T</span>
        <span className="tool pro">B</span>
        <span className="tool pro">R</span>
        <span className="tool pro">1</span>
      </div>
    </div>
  );
}
