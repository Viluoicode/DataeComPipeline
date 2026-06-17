using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using ECommerPipeline.Application.Payments;
using ECommerPipeline.Domain.Enums;
using Microsoft.Extensions.Options;

namespace ECommerPipeline.Infrastructure.Payments;

/// VNPay (sandbox) adapter. Pure HTTP: we build a signed redirect URL; VNPay sends
/// the customer back to our Return URL and (server-to-server) to our IPN URL with
/// the same signed params. Signature = HMAC-SHA512 over the sorted, URL-encoded
/// vnp_* params. Sandbox self-registration is free (sandbox.vnpayment.vn/devreg);
/// go-live just swaps TmnCode/HashSecret for a real merchant account.
public class VnPayGateway : IPaymentGateway
{
    private readonly VnPayOptions _opt;

    public VnPayGateway(IOptions<PaymentOptions> opt) => _opt = opt.Value.VnPay;

    public PaymentMethod Method => PaymentMethod.VnPay;

    public Task<PaymentInitResult> CreatePaymentAsync(PaymentInitContext ctx, CancellationToken ct = default)
    {
        if (!_opt.IsConfigured)
            throw new InvalidOperationException(
                "VNPay is not configured. Set Payments:VnPay:TmnCode and HashSecret (sandbox credentials are free).");

        var nowVn = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)); // VNPay uses GMT+7
        var p = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["vnp_Version"]   = "2.1.0",
            ["vnp_Command"]   = "pay",
            ["vnp_TmnCode"]   = _opt.TmnCode,
            ["vnp_Amount"]    = ((long)(ctx.Amount * 100m)).ToString(CultureInfo.InvariantCulture), // VND ×100
            ["vnp_CurrCode"]  = "VND",
            ["vnp_TxnRef"]    = ctx.ProviderTxnRef,
            ["vnp_OrderInfo"] = $"Thanh toan don hang {ctx.OrderNumber}",
            ["vnp_OrderType"] = "other",
            ["vnp_Locale"]    = string.IsNullOrWhiteSpace(ctx.Locale) ? "vn" : ctx.Locale,
            ["vnp_ReturnUrl"] = _opt.ReturnUrl,
            ["vnp_IpAddr"]    = string.IsNullOrWhiteSpace(ctx.IpAddress) ? "127.0.0.1" : ctx.IpAddress,
            ["vnp_CreateDate"] = nowVn.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture),
            ["vnp_ExpireDate"] = nowVn.AddMinutes(15).ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture),
        };

        var signData = BuildQuery(p);
        var secureHash = HmacSha512(_opt.HashSecret, signData);
        var url = $"{_opt.BaseUrl}?{signData}&vnp_SecureHash={secureHash}";

        return Task.FromResult(new PaymentInitResult(url));
    }

    public PaymentCallbackResult VerifyCallback(IReadOnlyDictionary<string, string> data)
    {
        var provided = data.TryGetValue("vnp_SecureHash", out var h) ? h : "";

        // Hash is computed over all vnp_* params except the hash fields themselves.
        var signed = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (k, v) in data)
        {
            if (k is "vnp_SecureHash" or "vnp_SecureHashType") continue;
            if (k.StartsWith("vnp_", StringComparison.Ordinal) && !string.IsNullOrEmpty(v))
                signed[k] = v;
        }

        var computed = HmacSha512(_opt.HashSecret, BuildQuery(signed));
        var signatureValid = !string.IsNullOrEmpty(provided)
            && computed.Equals(provided, StringComparison.OrdinalIgnoreCase);

        var responseCode = data.TryGetValue("vnp_ResponseCode", out var rc) ? rc : "";
        var txnStatus    = data.TryGetValue("vnp_TransactionStatus", out var ts) ? ts : "";
        var txnRef       = data.TryGetValue("vnp_TxnRef", out var tr) ? tr : "";
        var gatewayTxn   = data.TryGetValue("vnp_TransactionNo", out var gt) ? gt : null;
        var amount       = data.TryGetValue("vnp_Amount", out var am)
                           && long.TryParse(am, NumberStyles.Integer, CultureInfo.InvariantCulture, out var raw)
                           ? raw / 100m : 0m;

        var success = signatureValid && responseCode == "00" && txnStatus == "00";

        return new PaymentCallbackResult(
            SignatureValid: signatureValid,
            Success: success,
            ProviderTxnRef: txnRef,
            GatewayTransactionId: gatewayTxn,
            Amount: amount,
            ResponseCode: responseCode,
            RawData: BuildQuery(new SortedDictionary<string, string>(data.ToDictionary(x => x.Key, x => x.Value), StringComparer.Ordinal)));
    }

    // "key=urlencode(value)" joined by "&", in ordinal key order — exactly the
    // string VNPay signs, so request-build and callback-verify stay byte-identical.
    private static string BuildQuery(SortedDictionary<string, string> p)
    {
        var sb = new StringBuilder();
        foreach (var (k, v) in p)
        {
            if (string.IsNullOrEmpty(v)) continue;
            if (sb.Length > 0) sb.Append('&');
            sb.Append(WebUtility.UrlEncode(k)).Append('=').Append(WebUtility.UrlEncode(v));
        }
        return sb.ToString();
    }

    private static string HmacSha512(string key, string data)
    {
        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(key));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
