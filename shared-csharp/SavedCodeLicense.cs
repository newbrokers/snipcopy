using System;
using System.IO;
using System.Numerics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SavedCode.Licensing
{
    public sealed class SavedCodeLicenseOptions
    {
        public const string DefaultPublicKeyRawBase64 = "UqcQjsC1QuHqNc9OdBmCFAayLFWw0aNDwj6CmhQgJOU=";

        public string ProductSlug;
        public string ProductName;
        public string PublicKeyRawBase64 = DefaultPublicKeyRawBase64;
        public string ApiBaseUrl;

        public SavedCodeLicenseOptions(string productSlug, string productName)
        {
            ProductSlug = NormalizeProductSlug(productSlug);
            ProductName = String.IsNullOrWhiteSpace(productName) ? ProductSlug : productName.Trim();
        }

        public string ResolvedApiBaseUrl
        {
            get
            {
                string value = ApiBaseUrl;
                if (String.IsNullOrWhiteSpace(value))
                {
                    value = Environment.GetEnvironmentVariable("SAVEDCODE_API_BASE_URL");
                }

                if (String.IsNullOrWhiteSpace(value)) return "https://www.savedcode.com";
                value = value.Trim().TrimEnd('/');
                if (String.Equals(value, "https://savedcode.com", StringComparison.OrdinalIgnoreCase)) return "https://www.savedcode.com";
                if (String.Equals(value, "http://savedcode.com", StringComparison.OrdinalIgnoreCase)) return "https://www.savedcode.com";
                return value;
            }
        }

        public static string NormalizeProductSlug(string productSlug)
        {
            string value = (productSlug ?? "").Trim().ToLowerInvariant();
            if (value == "snipcopy" || value == "draw-overlay" || value == "audio-crop") return value;
            throw new ArgumentException("Unknown SavedCode product slug: " + productSlug);
        }
    }

    public sealed class SavedCodeLicenseInfo
    {
        public string Key = "";
        public string CustomerEmail = "";
        public string ProductSlug = "";
        public string Status = "";
        public string Token = "";
        public string Reason = "";
        public DateTime ExpiresAt = DateTime.MinValue;

        public bool IsActiveFor(string productSlug)
        {
            return !String.IsNullOrEmpty(Key)
                && String.Equals(ProductSlug, SavedCodeLicenseOptions.NormalizeProductSlug(productSlug), StringComparison.OrdinalIgnoreCase)
                && String.Equals(Status, "active", StringComparison.OrdinalIgnoreCase)
                && ExpiresAt.ToUniversalTime() >= DateTime.UtcNow;
        }

        public string DisplayText(string productName)
        {
            if (IsActiveFor(ProductSlug)) return (String.IsNullOrWhiteSpace(productName) ? "Pro" : productName + " Pro") + " until " + ExpiresAt.ToLocalTime().ToString("yyyy-MM-dd");
            if (!String.IsNullOrEmpty(Reason)) return "Free - " + Reason;
            if (!String.IsNullOrEmpty(Key)) return "Free - Expired";
            return "Free";
        }
    }

    public sealed class SavedCodeLicenseClient
    {
        private readonly SavedCodeLicenseOptions options;

        public SavedCodeLicenseInfo Current { get; private set; }

        public SavedCodeLicenseClient(SavedCodeLicenseOptions options)
        {
            if (options == null) throw new ArgumentNullException("options");
            this.options = options;
            Current = Load();
        }

        public SavedCodeLicenseInfo Load()
        {
            SavedCodeLicenseInfo saved = LoadSavedCodeRecord();
            Current = saved ?? new SavedCodeLicenseInfo { ProductSlug = options.ProductSlug };
            return Current;
        }

        public bool Activate(string email, string licenseKey, out string message)
        {
            try
            {
                string body = "{"
                    + "\"licenseKey\":\"" + JsonEscape((licenseKey ?? "").Trim()) + "\","
                    + "\"email\":\"" + JsonEscape((email ?? "").Trim()) + "\","
                    + "\"product_slug\":\"" + JsonEscape(options.ProductSlug) + "\","
                    + "\"machineHash\":\"" + MachineHash() + "\""
                    + "}";
                string json = PostJson(options.ResolvedApiBaseUrl + "/api/license/activate", body);
                SavedCodeLicenseInfo info;
                string reason;
                if (!VerifyTokenFromResponse(json, out info, out reason))
                {
                    message = reason;
                    return false;
                }

                SaveSavedCodeRecord(info);
                Current = info;
                message = options.ProductName + " Pro is active until " + info.ExpiresAt.ToLocalTime().ToString("yyyy-MM-dd") + ".";
                return true;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
        }

        public bool Sync(out string message)
        {
            if (Current == null || String.IsNullOrEmpty(Current.Key))
            {
                message = "Activate a SavedCode license first.";
                return false;
            }

            try
            {
                string body = "{"
                    + "\"licenseKey\":\"" + JsonEscape(Current.Key) + "\","
                    + "\"product_slug\":\"" + JsonEscape(options.ProductSlug) + "\","
                    + "\"machineHash\":\"" + MachineHash() + "\""
                    + "}";
                string json = PostJson(options.ResolvedApiBaseUrl + "/api/license/sync", body);
                SavedCodeLicenseInfo info;
                string reason;
                if (!VerifyTokenFromResponse(json, out info, out reason))
                {
                    message = reason;
                    return false;
                }

                SaveSavedCodeRecord(info);
                Current = info;
                message = "License synced. " + options.ProductName + " Pro is active until " + info.ExpiresAt.ToLocalTime().ToString("yyyy-MM-dd") + ".";
                return true;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
        }

        public void Deactivate()
        {
            Current = new SavedCodeLicenseInfo { ProductSlug = options.ProductSlug };
            string path = GetSavedCodePath();
            if (File.Exists(path)) File.Delete(path);
        }

        public bool IsPro
        {
            get { return Current != null && Current.IsActiveFor(options.ProductSlug); }
        }

        private SavedCodeLicenseInfo LoadSavedCodeRecord()
        {
            string path = GetSavedCodePath();
            if (!File.Exists(path)) return null;

            try
            {
                string envelope = File.ReadAllText(path);
                string protectedData = ExtractJsonString(envelope, "protected_data");
                if (String.IsNullOrEmpty(protectedData)) return new SavedCodeLicenseInfo { ProductSlug = options.ProductSlug };

                byte[] raw = ProtectedData.Unprotect(Convert.FromBase64String(protectedData), null, DataProtectionScope.CurrentUser);
                string record = Encoding.UTF8.GetString(raw);
                string token = ExtractJsonString(record, "token");
                string savedKey = ExtractJsonString(record, "license_key");
                string savedEmail = ExtractJsonString(record, "customer_email");
                SavedCodeLicenseInfo info;
                string reason;
                if (!SavedCodeLicenseTokenVerifier.TryVerify(token, options.ProductSlug, options.PublicKeyRawBase64, out info, out reason))
                {
                    return new SavedCodeLicenseInfo { Key = savedKey, CustomerEmail = savedEmail, ProductSlug = options.ProductSlug, Reason = reason };
                }
                if (!String.IsNullOrEmpty(savedKey)) info.Key = savedKey;
                if (!String.IsNullOrEmpty(savedEmail)) info.CustomerEmail = savedEmail;
                info.Token = token;
                return info;
            }
            catch
            {
                return new SavedCodeLicenseInfo { ProductSlug = options.ProductSlug };
            }
        }

        private bool VerifyTokenFromResponse(string json, out SavedCodeLicenseInfo info, out string reason)
        {
            string token = ExtractJsonString(json, "token");
            if (String.IsNullOrEmpty(token))
            {
                info = null;
                reason = "SavedCode did not return a license token.";
                return false;
            }
            if (!SavedCodeLicenseTokenVerifier.TryVerify(token, options.ProductSlug, options.PublicKeyRawBase64, out info, out reason))
            {
                return false;
            }
            info.Token = token;
            return true;
        }

        private static string PostJson(string url, string body)
        {
            return PostJson(url, body, 0);
        }

        private static string PostJson(string url, string body, int redirectCount)
        {
            ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol | (SecurityProtocolType)3072;
            byte[] data = Encoding.UTF8.GetBytes(body);
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Accept = "application/json";
            request.Timeout = 20000;
            request.AllowAutoRedirect = false;
            request.ContentLength = data.Length;
            using (Stream stream = request.GetRequestStream())
            {
                stream.Write(data, 0, data.Length);
            }

            try
            {
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    if (IsRedirectStatus(response) && redirectCount < 3)
                    {
                        string redirectUrl = ResolveRedirectUrl(url, response.Headers["Location"]);
                        if (!String.IsNullOrEmpty(redirectUrl)) return PostJson(redirectUrl, body, redirectCount + 1);
                    }

                    using (var reader = new StreamReader(response.GetResponseStream()))
                    {
                        return reader.ReadToEnd();
                    }
                }
            }
            catch (WebException ex)
            {
                var response = ex.Response as HttpWebResponse;
                if (response != null && IsRedirectStatus(response) && redirectCount < 3)
                {
                    string redirectUrl = ResolveRedirectUrl(url, response.Headers["Location"]);
                    response.Close();
                    if (!String.IsNullOrEmpty(redirectUrl)) return PostJson(redirectUrl, body, redirectCount + 1);
                }

                string error = "";
                if (ex.Response != null)
                {
                    using (var reader = new StreamReader(ex.Response.GetResponseStream()))
                    {
                        error = ExtractJsonString(reader.ReadToEnd(), "error");
                    }
                }
                if (String.IsNullOrEmpty(error)) error = ex.Message;
                throw new ApplicationException(error);
            }
        }

        private static bool IsRedirectStatus(HttpWebResponse response)
        {
            int status = (int)response.StatusCode;
            return status == 301 || status == 302 || status == 303 || status == 307 || status == 308;
        }

        private static string ResolveRedirectUrl(string currentUrl, string location)
        {
            if (String.IsNullOrWhiteSpace(location)) return "";
            Uri redirectUri;
            if (Uri.TryCreate(location, UriKind.Absolute, out redirectUri)) return redirectUri.ToString();

            Uri currentUri;
            if (Uri.TryCreate(currentUrl, UriKind.Absolute, out currentUri) && Uri.TryCreate(currentUri, location, out redirectUri))
            {
                return redirectUri.ToString();
            }
            return "";
        }

        private void SaveSavedCodeRecord(SavedCodeLicenseInfo info)
        {
            string path = GetSavedCodePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string json = "{"
                + "\"license_key\":\"" + JsonEscape(info.Key) + "\","
                + "\"customer_email\":\"" + JsonEscape(info.CustomerEmail) + "\","
                + "\"token\":\"" + JsonEscape(info.Token) + "\","
                + "\"activated_at\":\"" + DateTime.UtcNow.ToString("o") + "\""
                + "}";
            byte[] protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(json), null, DataProtectionScope.CurrentUser);
            File.WriteAllText(path, "{\"protected_data\":\"" + Convert.ToBase64String(protectedBytes) + "\"}");
        }

        private string GetSavedCodePath()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SavedCode", "Licenses");
            return Path.Combine(dir, options.ProductSlug + ".json");
        }

        private static string MachineHash()
        {
            string text = Environment.MachineName + "|" + Environment.OSVersion.VersionString + "|" + Environment.UserDomainName;
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        internal static string ExtractJsonString(string json, string name)
        {
            if (String.IsNullOrEmpty(json)) return "";
            Match match = Regex.Match(json, "\"" + Regex.Escape(name) + "\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
            if (!match.Success) return "";
            return JsonUnescape(match.Groups[1].Value);
        }

        private static string JsonEscape(string value)
        {
            if (value == null) return "";
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
        }

        private static string JsonUnescape(string value)
        {
            var builder = new StringBuilder();
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c != '\\' || i + 1 >= value.Length)
                {
                    builder.Append(c);
                    continue;
                }

                char next = value[++i];
                if (next == '"' || next == '\\' || next == '/') builder.Append(next);
                else if (next == 'b') builder.Append('\b');
                else if (next == 'f') builder.Append('\f');
                else if (next == 'n') builder.Append('\n');
                else if (next == 'r') builder.Append('\r');
                else if (next == 't') builder.Append('\t');
                else if (next == 'u' && i + 4 < value.Length)
                {
                    string hex = value.Substring(i + 1, 4);
                    int code;
                    if (Int32.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out code))
                    {
                        builder.Append((char)code);
                        i += 4;
                    }
                }
            }
            return builder.ToString();
        }
    }

    internal static class SavedCodeLicenseTokenVerifier
    {
        private struct EdPoint
        {
            public BigInteger X;
            public BigInteger Y;

            public EdPoint(BigInteger x, BigInteger y)
            {
                X = x;
                Y = y;
            }
        }

        private static readonly BigInteger P = BigInteger.Pow(new BigInteger(2), 255) - 19;
        private static readonly BigInteger L = BigInteger.Pow(new BigInteger(2), 252) + BigInteger.Parse("27742317777372353535851937790883648493");
        private static readonly BigInteger D = Mod(BigInteger.Parse("-121665") * Invert(BigInteger.Parse("121666")));
        private static readonly BigInteger I = BigInteger.ModPow(new BigInteger(2), (P - 1) / 4, P);
        private static readonly EdPoint Identity = new EdPoint(BigInteger.Zero, BigInteger.One);
        private static readonly EdPoint BasePoint = new EdPoint(
            BigInteger.Parse("15112221349535400772501151409588531511454012693041857206046113283949847762202"),
            BigInteger.Parse("46316835694926478169428394003475163141307993866256225615783033603165251855960"));

        public static bool TryVerify(string token, string expectedProductSlug, string publicKeyRawBase64, out SavedCodeLicenseInfo info, out string reason)
        {
            info = null;
            reason = "Invalid license token.";

            if (String.IsNullOrEmpty(token))
            {
                reason = "No local license token.";
                return false;
            }

            string[] parts = token.Split('.');
            if (parts.Length != 2)
            {
                reason = "Malformed license token.";
                return false;
            }

            byte[] publicKey;
            byte[] signature;
            byte[] payloadBytes;
            try
            {
                publicKey = Convert.FromBase64String(publicKeyRawBase64);
                signature = Base64UrlDecode(parts[1]);
                payloadBytes = Base64UrlDecode(parts[0]);
            }
            catch
            {
                reason = "Malformed license token.";
                return false;
            }

            byte[] signedBody = Encoding.ASCII.GetBytes(parts[0]);
            if (!VerifyEd25519(publicKey, signedBody, signature))
            {
                reason = "Invalid license signature.";
                return false;
            }

            string payload = Encoding.UTF8.GetString(payloadBytes);
            string productSlug = SavedCodeLicenseClient.ExtractJsonString(payload, "product_slug");
            string status = SavedCodeLicenseClient.ExtractJsonString(payload, "status");
            string expiresText = SavedCodeLicenseClient.ExtractJsonString(payload, "expires_at");
            string licenseKey = SavedCodeLicenseClient.ExtractJsonString(payload, "license_key");
            string email = SavedCodeLicenseClient.ExtractJsonString(payload, "customer_email");

            if (!String.Equals(productSlug, SavedCodeLicenseOptions.NormalizeProductSlug(expectedProductSlug), StringComparison.OrdinalIgnoreCase))
            {
                reason = "License belongs to a different product.";
                return false;
            }
            if (!String.Equals(status, "active", StringComparison.OrdinalIgnoreCase))
            {
                reason = "License status is " + (String.IsNullOrEmpty(status) ? "unknown" : status) + ".";
                return false;
            }

            DateTime expiresAt;
            if (!DateTime.TryParse(expiresText, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out expiresAt))
            {
                reason = "License expiry is missing or invalid.";
                return false;
            }
            expiresAt = expiresAt.ToUniversalTime();
            if (expiresAt < DateTime.UtcNow)
            {
                reason = "License expired.";
                info = new SavedCodeLicenseInfo
                {
                    Key = licenseKey,
                    CustomerEmail = email,
                    ProductSlug = productSlug,
                    Status = status,
                    ExpiresAt = expiresAt,
                    Reason = reason
                };
                return false;
            }

            info = new SavedCodeLicenseInfo
            {
                Key = licenseKey,
                CustomerEmail = email,
                ProductSlug = productSlug,
                Status = status,
                ExpiresAt = expiresAt
            };
            reason = "Active";
            return true;
        }

        private static bool VerifyEd25519(byte[] publicKey, byte[] message, byte[] signature)
        {
            if (publicKey == null || publicKey.Length != 32 || signature == null || signature.Length != 64) return false;

            byte[] rBytes = new byte[32];
            byte[] sBytes = new byte[32];
            Buffer.BlockCopy(signature, 0, rBytes, 0, 32);
            Buffer.BlockCopy(signature, 32, sBytes, 0, 32);

            BigInteger s = FromLittleEndian(sBytes);
            if (s < 0 || s >= L) return false;

            EdPoint a;
            EdPoint r;
            if (!DecodePoint(publicKey, out a)) return false;
            if (!DecodePoint(rBytes, out r)) return false;

            byte[] hashInput = new byte[64 + message.Length];
            Buffer.BlockCopy(rBytes, 0, hashInput, 0, 32);
            Buffer.BlockCopy(publicKey, 0, hashInput, 32, 32);
            Buffer.BlockCopy(message, 0, hashInput, 64, message.Length);

            BigInteger h;
            using (SHA512 sha = SHA512.Create())
            {
                h = FromLittleEndian(sha.ComputeHash(hashInput)) % L;
            }

            EdPoint left = ScalarMultiply(s, BasePoint);
            EdPoint right = Add(r, ScalarMultiply(h, a));
            return Equal(left, right);
        }

        private static bool DecodePoint(byte[] encoded, out EdPoint point)
        {
            point = Identity;
            if (encoded == null || encoded.Length != 32) return false;

            byte[] yBytes = new byte[32];
            Buffer.BlockCopy(encoded, 0, yBytes, 0, 32);
            int sign = (yBytes[31] & 0x80) != 0 ? 1 : 0;
            yBytes[31] = (byte)(yBytes[31] & 0x7f);

            BigInteger y = FromLittleEndian(yBytes);
            if (y >= P) return false;

            BigInteger yy = Mod(y * y);
            BigInteger xx = Mod((yy - 1) * Invert(Mod(D * yy + 1)));
            BigInteger x = BigInteger.ModPow(xx, (P + 3) / 8, P);
            if (Mod(x * x - xx) != 0) x = Mod(x * I);
            if (Mod(x * x - xx) != 0) return false;
            if (x.IsZero && sign == 1) return false;
            if ((x.IsEven ? 0 : 1) != sign) x = Mod(P - x);

            point = new EdPoint(x, y);
            return true;
        }

        private static EdPoint Add(EdPoint p, EdPoint q)
        {
            BigInteger x1x2 = Mod(p.X * q.X);
            BigInteger y1y2 = Mod(p.Y * q.Y);
            BigInteger dxxyy = Mod(D * x1x2 * y1y2);
            BigInteger x = Mod((p.X * q.Y + q.X * p.Y) * Invert(Mod(1 + dxxyy)));
            BigInteger y = Mod((y1y2 + x1x2) * Invert(Mod(1 - dxxyy)));
            return new EdPoint(x, y);
        }

        private static EdPoint ScalarMultiply(BigInteger scalar, EdPoint point)
        {
            EdPoint result = Identity;
            EdPoint addend = point;
            while (scalar > 0)
            {
                if (!scalar.IsEven) result = Add(result, addend);
                addend = Add(addend, addend);
                scalar = scalar >> 1;
            }
            return result;
        }

        private static bool Equal(EdPoint a, EdPoint b)
        {
            return Mod(a.X - b.X).IsZero && Mod(a.Y - b.Y).IsZero;
        }

        private static BigInteger Invert(BigInteger value)
        {
            return BigInteger.ModPow(Mod(value), P - 2, P);
        }

        private static BigInteger Mod(BigInteger value)
        {
            value %= P;
            if (value < 0) value += P;
            return value;
        }

        private static BigInteger FromLittleEndian(byte[] bytes)
        {
            byte[] positive = new byte[bytes.Length + 1];
            Buffer.BlockCopy(bytes, 0, positive, 0, bytes.Length);
            positive[bytes.Length] = 0;
            return new BigInteger(positive);
        }

        private static byte[] Base64UrlDecode(string value)
        {
            string padded = value.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2:
                    padded += "==";
                    break;
                case 3:
                    padded += "=";
                    break;
                case 0:
                    break;
                default:
                    throw new FormatException("Invalid base64url.");
            }
            return Convert.FromBase64String(padded);
        }
    }
}
