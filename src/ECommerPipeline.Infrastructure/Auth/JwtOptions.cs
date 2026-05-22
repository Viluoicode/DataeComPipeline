namespace ECommerPipeline.Infrastructure.Auth;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Secret    { get; set; } = null!;
    public string Issuer    { get; set; } = "ECommerPipeline";
    public string Audience  { get; set; } = "ECommerPipeline.Client";
    public int AccessTokenMinutes  { get; set; } = 60;       // 1 hour
    public int RefreshTokenDays    { get; set; } = 7;
}
