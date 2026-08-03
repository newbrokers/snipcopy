from __future__ import annotations

import base64
import ctypes
import ctypes.wintypes
import hashlib
import json
import os
import platform
import urllib.error
import urllib.request
import uuid
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from cryptography.exceptions import InvalidSignature
from cryptography.hazmat.primitives.serialization import load_pem_public_key


SAVEDCODE_API_BASE_URL = os.environ.get("SAVEDCODE_API_BASE_URL", "https://www.savedcode.com").rstrip("/")
SAVEDCODE_PUBLIC_KEY_PEM_BASE64 = (
    os.environ.get("SAVEDCODE_PUBLIC_KEY_PEM_BASE64")
    or "LS0tLS1CRUdJTiBQVUJMSUMgS0VZLS0tLS0KTUNvd0JRWURLMlZ3QXlFQVVxY1Fqc0MxUXVIcU5jOU9kQm1DRkFheUxGV3cwYU5Ed2o2Q21oUWdKT1U9Ci0tLS0tRU5EIFBVQkxJQyBLRVktLS0tLQo="
)
PRODUCT_SLUGS = {"snipcopy", "draw-overlay", "audio-crop"}
USABLE_STATUSES = {"active"}


class SavedCodeLicenseError(Exception):
    pass


@dataclass(frozen=True)
class LicenseStatus:
    is_pro: bool
    reason: str
    product_slug: str
    license_key: str | None = None
    customer_email: str | None = None
    expires_at: datetime | None = None

    @property
    def display_text(self) -> str:
        if self.is_pro and self.expires_at:
            return f"Pro active until {self.expires_at.date().isoformat()}"
        if self.license_key:
            return f"Free - {self.reason}"
        return "Free"


def normalize_product_slug(product_slug: str) -> str:
    normalized = (product_slug or "").strip().lower()
    if normalized not in PRODUCT_SLUGS:
        raise SavedCodeLicenseError(f"Unknown SavedCode product: {product_slug}")
    return normalized


def get_storage_path(product_slug: str) -> Path:
    product_slug = normalize_product_slug(product_slug)
    base = os.environ.get("APPDATA")
    root = Path(base) if base else Path.home() / ".savedcode"
    return root / "SavedCode" / "Licenses" / f"{product_slug}.json"


def machine_hash() -> str:
    parts = [
        platform.node(),
        platform.system(),
        platform.release(),
        str(uuid.getnode()),
    ]
    return hashlib.sha256("|".join(parts).encode("utf-8")).hexdigest()


def activate_license(license_key: str, email: str, product_slug: str, api_base_url: str = SAVEDCODE_API_BASE_URL) -> LicenseStatus:
    product_slug = normalize_product_slug(product_slug)
    response = _post_json(
        f"{api_base_url.rstrip('/')}/api/license/activate",
        {
            "licenseKey": license_key.strip(),
            "email": email.strip(),
            "product_slug": product_slug,
            "machineHash": machine_hash(),
        },
    )
    token = response.get("token")
    if not isinstance(token, str):
        raise SavedCodeLicenseError("SavedCode did not return a license token.")

    status = verify_token(token, product_slug)
    if not status.is_pro:
        raise SavedCodeLicenseError(status.reason)

    _save_record(
        product_slug,
        {
            "license_key": response.get("licenseKey") or license_key.strip(),
            "customer_email": email.strip().lower(),
            "token": token,
            "payload": response.get("payload"),
            "activated_at": datetime.now(timezone.utc).isoformat(),
        },
    )
    return status


def sync_license(product_slug: str, api_base_url: str = SAVEDCODE_API_BASE_URL) -> LicenseStatus:
    product_slug = normalize_product_slug(product_slug)
    record = load_record(product_slug)
    license_key = record.get("license_key")
    if not license_key:
        raise SavedCodeLicenseError("No saved license key. Activate first.")

    response = _post_json(
        f"{api_base_url.rstrip('/')}/api/license/sync",
        {
            "licenseKey": license_key,
            "product_slug": product_slug,
            "machineHash": machine_hash(),
        },
    )
    token = response.get("token")
    if not isinstance(token, str):
        raise SavedCodeLicenseError("SavedCode did not return a license token.")

    status = verify_token(token, product_slug)
    if not status.is_pro:
        raise SavedCodeLicenseError(status.reason)

    record.update(
        {
            "license_key": response.get("licenseKey") or license_key,
            "token": token,
            "payload": response.get("payload"),
            "synced_at": datetime.now(timezone.utc).isoformat(),
        }
    )
    _save_record(product_slug, record)
    return status


def get_license_status(product_slug: str) -> LicenseStatus:
    product_slug = normalize_product_slug(product_slug)
    record = load_record(product_slug)
    token = record.get("token")
    if not isinstance(token, str) or not token:
        return LicenseStatus(False, "No local license token", product_slug)

    return verify_token(token, product_slug)


def deactivate_license(product_slug: str) -> None:
    path = get_storage_path(product_slug)
    if path.exists():
        path.unlink()


def load_record(product_slug: str) -> dict[str, Any]:
    path = get_storage_path(product_slug)
    if not path.exists():
        return {}
    try:
        envelope = json.loads(path.read_text(encoding="utf-8"))
        protected_data = envelope.get("protected_data")
        if not isinstance(protected_data, str):
            return {}
        raw = _unprotect(base64.b64decode(protected_data))
        data = json.loads(raw.decode("utf-8"))
        return data if isinstance(data, dict) else {}
    except Exception:
        return {}


def verify_token(token: str, product_slug: str) -> LicenseStatus:
    product_slug = normalize_product_slug(product_slug)
    try:
        body_b64, signature_b64 = token.split(".", 1)
        signature = _base64url_decode(signature_b64)
        body_bytes = body_b64.encode("ascii")
        public_key = _load_public_key()
        public_key.verify(signature, body_bytes)
        payload = json.loads(_base64url_decode(body_b64).decode("utf-8"))
    except InvalidSignature:
        return LicenseStatus(False, "Invalid license signature", product_slug)
    except Exception as exc:
        return LicenseStatus(False, f"Invalid license token: {exc}", product_slug)

    if payload.get("product_slug") != product_slug:
        return LicenseStatus(False, "License belongs to a different product", product_slug, payload.get("license_key"))

    status = payload.get("status")
    expires_at = _parse_datetime(payload.get("expires_at"))
    if not expires_at:
        return LicenseStatus(False, "License expiry is missing or invalid", product_slug, payload.get("license_key"))
    if expires_at < datetime.now(timezone.utc):
        return LicenseStatus(False, "License expired", product_slug, payload.get("license_key"), payload.get("customer_email"), expires_at)
    if status not in USABLE_STATUSES:
        return LicenseStatus(False, f"License status is {status}", product_slug, payload.get("license_key"), payload.get("customer_email"), expires_at)

    return LicenseStatus(True, "Active", product_slug, payload.get("license_key"), payload.get("customer_email"), expires_at)


def _save_record(product_slug: str, record: dict[str, Any]) -> None:
    path = get_storage_path(product_slug)
    path.parent.mkdir(parents=True, exist_ok=True)
    raw = json.dumps(record, separators=(",", ":")).encode("utf-8")
    protected = base64.b64encode(_protect(raw)).decode("ascii")
    path.write_text(json.dumps({"protected_data": protected}, indent=2), encoding="utf-8")


def _load_public_key():
    pem = base64.b64decode(SAVEDCODE_PUBLIC_KEY_PEM_BASE64)
    return load_pem_public_key(pem)


def _base64url_decode(value: str) -> bytes:
    padding = "=" * (-len(value) % 4)
    return base64.urlsafe_b64decode(value + padding)


def _parse_datetime(value: Any) -> datetime | None:
    if not isinstance(value, str):
        return None
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
        if parsed.tzinfo is None:
            return parsed.replace(tzinfo=timezone.utc)
        return parsed.astimezone(timezone.utc)
    except ValueError:
        return None


def _post_json(url: str, payload: dict[str, Any]) -> dict[str, Any]:
    data = json.dumps(payload).encode("utf-8")
    request = urllib.request.Request(
        url,
        data=data,
        headers={"Content-Type": "application/json", "Accept": "application/json"},
        method="POST",
    )
    try:
        with urllib.request.urlopen(request, timeout=20) as response:
            parsed = json.loads(response.read().decode("utf-8"))
    except urllib.error.HTTPError as exc:
        try:
            body = json.loads(exc.read().decode("utf-8"))
            message = body.get("error") or str(exc)
        except Exception:
            message = str(exc)
        raise SavedCodeLicenseError(message) from exc
    except urllib.error.URLError as exc:
        raise SavedCodeLicenseError(f"Could not reach SavedCode: {exc.reason}") from exc

    if not isinstance(parsed, dict):
        raise SavedCodeLicenseError("SavedCode returned an invalid response.")
    if "error" in parsed:
        raise SavedCodeLicenseError(str(parsed["error"]))
    return parsed


if os.name == "nt":
    class _DataBlob(ctypes.Structure):
        _fields_ = [
            ("cbData", ctypes.wintypes.DWORD),
            ("pbData", ctypes.POINTER(ctypes.c_byte)),
        ]

    def _blob_from_bytes(data: bytes) -> tuple[_DataBlob, ctypes.Array]:
        buffer = ctypes.create_string_buffer(data)
        blob = _DataBlob(len(data), ctypes.cast(buffer, ctypes.POINTER(ctypes.c_byte)))
        return blob, buffer

    def _bytes_from_blob(blob: _DataBlob) -> bytes:
        return ctypes.string_at(blob.pbData, blob.cbData)

    def _protect(data: bytes) -> bytes:
        in_blob, in_buffer = _blob_from_bytes(data)
        out_blob = _DataBlob()
        _ = in_buffer
        if not ctypes.windll.crypt32.CryptProtectData(ctypes.byref(in_blob), None, None, None, None, 0, ctypes.byref(out_blob)):
            raise SavedCodeLicenseError("Windows could not protect the license token.")
        try:
            return _bytes_from_blob(out_blob)
        finally:
            ctypes.windll.kernel32.LocalFree(out_blob.pbData)

    def _unprotect(data: bytes) -> bytes:
        in_blob, in_buffer = _blob_from_bytes(data)
        out_blob = _DataBlob()
        _ = in_buffer
        if not ctypes.windll.crypt32.CryptUnprotectData(ctypes.byref(in_blob), None, None, None, None, 0, ctypes.byref(out_blob)):
            raise SavedCodeLicenseError("Windows could not read the protected license token.")
        try:
            return _bytes_from_blob(out_blob)
        finally:
            ctypes.windll.kernel32.LocalFree(out_blob.pbData)
else:
    def _protect(data: bytes) -> bytes:
        return data

    def _unprotect(data: bytes) -> bytes:
        return data
