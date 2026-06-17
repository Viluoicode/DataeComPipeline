# Payments (VNPay / MoMo)

Online checkout uses a provider-agnostic `IPaymentGateway` abstraction with two
adapters: **VNPay** and **MoMo**. Cash-on-delivery (COD) needs no gateway and
always works. Online methods stay **disabled in the storefront UI until their
sandbox credentials are configured** (the `GET /api/payments/methods` endpoint
reports which are ready).

## Is it free? (yes, for sandbox/testing)

| | Sandbox / test | Go-live (production) |
|---|---|---|
| **VNPay** | Free self-registration at <https://sandbox.vnpayment.vn/devreg/> → instant `TmnCode` + `HashSecret`. No business licence. | Requires signed contract + business registration (giấy phép kinh doanh) + website verification. |
| **MoMo** | Free at <https://developers.momo.vn/> → `PartnerCode` / `AccessKey` / `SecretKey` for the Test environment (status "unverified"). Test wallet step needs the MoMo Test App, OTP `0000`. | Requires business onboarding. |

Going live is **only a credential swap** — no code changes.

## How the flow works

1. Customer checks out choosing VNPay/MoMo → `POST /api/orders` saves the order
   (`Pending` / `Unpaid`).
2. Frontend calls `POST /api/payments/{orderId}/create` → we create a `Payment`
   row (unique `ProviderTxnRef`), build the gateway redirect, and return its URL.
3. Browser is redirected to the gateway and pays.
4. The gateway sends the customer back to our **return URL** *and* (server-to-server)
   to our **IPN URL**. Both run the same idempotent handler:
   - signature verified (VNPay HMAC-SHA512, MoMo HMAC-SHA256),
   - amount checked against the reserved amount (anti-tamper),
   - on success: `Payment → Paid`, `Order.PaymentStatus → Paid`, and the order is
     auto-confirmed (`Pending → Confirmed`) with an `OrderEvent`.
   - A replayed callback for an already-paid order is a no-op (no double-credit).
5. The return endpoint bounces the browser to the SPA `/payment-result` page.

> On `localhost` the **return** URL works (it's a browser redirect) but the **IPN**
> won't (VNPay/MoMo can't reach localhost). The return handler also settles the
> payment, so local testing still works end-to-end.

## Configuration

Set under the `Payments` section (env vars use `__`). Secrets come from env in
Docker/prod, never committed.

```
Payments__FrontendResultUrl       # where to send the browser after settling
Payments__VnPay__TmnCode
Payments__VnPay__HashSecret
Payments__VnPay__ReturnUrl        # must point at /api/payments/vnpay/return
Payments__Momo__PartnerCode
Payments__Momo__AccessKey
Payments__Momo__SecretKey
Payments__Momo__ReturnUrl         # /api/payments/momo/return
Payments__Momo__IpnUrl            # /api/payments/momo/ipn (must be public for IPN)
```

- **Local dev** (`appsettings.json`): `FrontendResultUrl=http://localhost:5173/payment-result`, VNPay `ReturnUrl=http://localhost:5193/api/payments/vnpay/return`.
- **Docker** (`docker-compose.yml`): same-origin via nginx → `FrontendResultUrl=/payment-result`, `ReturnUrl=http://localhost/api/payments/vnpay/return`. Fill in the empty credential vars to enable.

### VNPay sandbox test cards
Use the test cards from the VNPay sandbox docs (e.g. NCB bank card `9704198526191432198`, holder `NGUYEN VAN A`, issue `07/15`, OTP `123456`).
