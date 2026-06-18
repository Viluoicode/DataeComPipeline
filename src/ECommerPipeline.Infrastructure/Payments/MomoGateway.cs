using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ECommerPipeline.Application.Payments;
using ECommerPipeline.Domain.Enums;
using Microsoft.Extensions.Options;

namespace ECommerPipeline.Infrastructure.Payments;

/// MoMo (test environment) adapter. Unlike VNPay, MoMo's "create" is a
/// server-to-server HTTP POST that returns a payUrl; the IPN is a signed JSON
/// POST back to us. Signatures are HMAC-SHA256 over a fixed field order.
/// Test credentials are free at developers.momo.vn (status "unverified" until
/// go-live); testing the wallet step needs the MoMo Test App (OTP 0000).
public class MomoGateway : IPaymentGateway
{
    private readonly MomoOptions _opt;
    private readonly IHttpClientFactory _httpFactory;

    public MomoGateway(IOptions<PaymentOptions> opt, IHttpClientFactory httpFactory)
    {
        _opt = opt.Value.Momo;
        _httpFactory = httpFactory;
    }

    public PaymentMethod Method => PaymentMethod.Momo;

    public async Task<PaymentInitResult> CreatePaymentAsync(PaymentInitContext ctx, CancellationToken ct = default)
    {
        if (!_opt.IsConfigured)
            throw new InvalidOperationException(
                "MoMo is not configured. Set Payments:Momo:PartnerCode/AccessKey/SecretKey (test credentials are free).");

        var requestId = Guid.NewGuid().ToString("N");
        var amount = ((long)ctx.Amount).ToString();
        var orderId = ctx.ProviderTxnRef;
        var orderInfo = $"Thanh toan don hang {ctx.OrderNumber}";
        const string extraData = "";
        const string requestType = "captureWallet";

        // Signature field order is fixed by MoMo's spec — do not reorder.
        var raw =
            $"accessKey={_opt.AccessKey}&amount={amount}&extraData={extraData}&ipnUrl={_opt.IpnUrl}" +
            $"&orderId={orderId}&orderInfo={orderInfo}&partnerCode={_opt.PartnerCode}" +
            $"&redirectUrl={_opt.ReturnUrl}&requestId={requestId}&requestType={requestType}";
        var signature = HmacSha256(_opt.SecretKey, raw);

        var body = new
        {
            partnerCode = _opt.PartnerCode,
            accessKey = _opt.AccessKey,
            requestId,
            amount,
            orderId,
            orderInfo,
            redirectUrl = _opt.ReturnUrl,
            ipnUrl = _opt.IpnUrl,
            extraData,
            requestType,
            signature,
            lang = "vi",
        };

        var client = _httpFactory.CreateClient();
        var resp = await client.PostAsJsonAsync(_opt.CreateUrl, body, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var resultCode = root.TryGetProperty("resultCode", out var rcEl) ? rcEl.GetInt32() : -1;
        var payUrl = root.TryGetProperty("payUrl", out var pu) ? pu.GetString() : null;

        if (resultCode != 0 || string.IsNullOrEmpty(payUrl))
        {
            var msg = root.TryGetProperty("message", out var m) ? m.GetString() : "unknown error";
            throw new InvalidOperationException($"MoMo create payment failed ({resultCode}): {msg}");
        }

        return new PaymentInitResult(payUrl);
    }

    public PaymentCallbackResult VerifyCallback(IReadOnlyDictionary<string, string> d)
    {
        string Get(string k) => d.TryGetValue(k, out var v) ? v : "";

        // MoMo IPN signature field order is fixed by spec.
        var raw =
            $"accessKey={_opt.AccessKey}&amount={Get("amount")}&extraData={Get("extraData")}" +
            $"&message={Get("message")}&orderId={Get("orderId")}&orderInfo={Get("orderInfo")}" +
            $"&orderType={Get("orderType")}&partnerCode={Get("partnerCode")}&payType={Get("payType")}" +
            $"&requestId={Get("requestId")}&responseTime={Get("responseTime")}" +
            $"&resultCode={Get("resultCode")}&transId={Get("transId")}";
        var computed = HmacSha256(_opt.SecretKey, raw);
        var provided = Get("signature");
        var signatureValid = !string.IsNullOrEmpty(provided)
            && computed.Equals(provided, StringComparison.OrdinalIgnoreCase);

        var resultCode = Get("resultCode");
        var amount = long.TryParse(Get("amount"), out var a) ? a : 0m;

        return new PaymentCallbackResult(
            SignatureValid: signatureValid,
            Success: signatureValid && resultCode == "0",
            ProviderTxnRef: Get("orderId"),
            GatewayTransactionId: Get("transId"),
            Amount: amount,
            ResponseCode: resultCode,
            RawData: JsonSerializer.Serialize(d));
    }

    private static string HmacSha256(string key, string data)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
