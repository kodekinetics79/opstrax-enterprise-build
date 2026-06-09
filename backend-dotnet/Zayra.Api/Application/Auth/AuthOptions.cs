namespace Zayra.Api.Application.Auth;

public class JwtOptions
{
    public string Issuer { get; set; } = "Zayra.Api";
    public string Audience { get; set; } = "Zayra.Client";
    public string SigningKey { get; set; } = "CHANGE_ME_TO_A_64_CHARACTER_PRODUCTION_SECRET_KEY";
    public int AccessTokenMinutes { get; set; } = 30;
    public int RefreshTokenDays { get; set; } = 14;
}

public class SeedAdminOptions
{
    public string TenantName { get; set; } = "Zayra HQ";
    public string TenantSlug { get; set; } = "zayra";
    public string Email { get; set; } = "admin@zayra.local";
    public string FullName { get; set; } = "Zayra Admin";
    public string Password { get; set; } = "ChangeMe123!";

    /// <summary>
    /// When true, seeds sample/demo business data (company, branches, departments,
    /// grades, sample policies and approval workflows). Defaults to FALSE so production
    /// tenants start clean — the admin configures their own organisation via Setup.
    /// </summary>
    public bool SeedDemoData { get; set; } = false;
}
