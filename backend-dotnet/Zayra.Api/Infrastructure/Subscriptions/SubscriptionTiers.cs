namespace Zayra.Api.Infrastructure.Subscriptions;

public static class SubscriptionTiers
{
    public static (int MaxUsers, int MaxEmployees) GetDefaults(string plan) => plan switch
    {
        "Trial"      => (3, 10),
        "Starter"    => (10, 50),
        "Growth"     => (50, 250),
        "Enterprise" => (0, 0),  // 0 = unlimited
        _            => (10, 50)
    };
}
