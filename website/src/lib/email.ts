export async function sendPortalLoginCode(email: string, code: string) {
  const apiKey = process.env.RESEND_API_KEY;
  const from = process.env.EMAIL_FROM;

  if (!apiKey || !from) {
    if (process.env.VERCEL_ENV === "production") {
      throw new Error("Email delivery is not configured. Set RESEND_API_KEY and EMAIL_FROM.");
    }
    return { sent: false, devCode: code };
  }

  const response = await fetch("https://api.resend.com/emails", {
    method: "POST",
    headers: {
      authorization: `Bearer ${apiKey}`,
      "content-type": "application/json",
      "user-agent": "SavedCode/1.0 (https://www.savedcode.com)"
    },
    body: JSON.stringify({
      from,
      to: email,
      subject: "Your SavedCode sign-in code",
      text: `Your SavedCode sign-in code is ${code}. It expires in 10 minutes.`,
      html: `<p>Your SavedCode sign-in code is:</p><p style="font-size:24px;font-weight:700;letter-spacing:4px">${code}</p><p>This code expires in 10 minutes.</p>`
    })
  });

  if (!response.ok) {
    throw new Error("Could not send the SavedCode sign-in email.");
  }

  return { sent: true };
}
