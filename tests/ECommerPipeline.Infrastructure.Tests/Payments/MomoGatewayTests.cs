using System.Security.Cryptography;
using System.Text;
using ECommerPipeline.Domain.Enums;
using ECommerPipeline.Infrastructure.Payments;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ECommerPipeline.Infrastructure.Tests.Payments;

public class MomoGatewayTests
{
    private const string AccessKey = "ACCESS_KEY_TEST";
    private const string SecretKey = "SECRET_KEY_TEST_0123456789";
    private const string PartnerCode = "MOMOTEST";

    private sealed class StubHttpFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();   // unused by VerifyCallback
    }

    private static MomoGateway NewGateway() =>
        new(Options.Create(new PaymentOptions
        {
            Momo = new MomoOptions { PartnerCode = PartnerCode, AccessKey = AccessKey, SecretKey = SecretKey }
        }), new StubHttpFactory());

    // Replicates MoMo's IPN signature field order so we can forge a valid callback.
    private static string Sign(Dictionary<string, string> d)
    {
        string G(string k) => d.TryGetValue(k, out var v) ? v : "";
        var raw =
            $"accessKey={AccessKey}&amount={G("amount")}&extraData={G("extraData")}" +
            $"&message={G("message")}&orderId={G("orderId")}&orderInfo={G("orderInfo")}" +
            $"&orderType={G("orderType")}&partnerCode={G("partnerCode")}&payType={G("payType")}" +
            $"&requestId={G("requestId")}&responseTime={G("responseTime")}" +
            $"&resultCode={G("resultCode")}&transId={G("transId")}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(SecretKey));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }

    private static Dictionary<string, string> SignedSuccessIpn()
    {
        var d = new Dictionary<string, string>
        {
            ["partnerCode"] = PartnerCode,
            ["orderId"] = "98765",
            ["requestId"] = "req-1",
            ["amount"] = "150000",
            ["orderInfo"] = "Thanh toan don hang ORD-1",
            ["orderType"] = "momo_wallet",
            ["transId"] = "2468013579",
            ["resultCode"] = "0",
            ["message"] = "Successful.",
            ["payType"] = "qr",
            ["responseTime"] = "1718000000000",
            ["extraData"] = "",
        };
        d["signature"] = Sign(d);
        return d;
    }

    [Fact]
    public void VerifyCallback_accepts_valid_signed_success()
    {
        var result = NewGateway().VerifyCallback(SignedSuccessIpn());

        result.SignatureValid.Should().BeTrue();
        result.Success.Should().BeTrue();
        result.ProviderTxnRef.Should().Be("98765");
        result.GatewayTransactionId.Should().Be("2468013579");
        result.Amount.Should().Be(150000m);
    }

    [Fact]
    public void VerifyCallback_rejects_tampered_amount()
    {
        var d = SignedSuccessIpn();
        d["amount"] = "1";   // tamper after signing

        var result = NewGateway().VerifyCallback(d);

        result.SignatureValid.Should().BeFalse();
        result.Success.Should().BeFalse();
    }

    [Fact]
    public void VerifyCallback_failed_resultcode_is_not_success_even_if_signed()
    {
        var d = new Dictionary<string, string>(SignedSuccessIpn());
        d["resultCode"] = "1006";   // user declined
        d["signature"] = Sign(d);   // re-sign so signature is valid

        var result = NewGateway().VerifyCallback(d);

        result.SignatureValid.Should().BeTrue();
        result.Success.Should().BeFalse();
    }
}
