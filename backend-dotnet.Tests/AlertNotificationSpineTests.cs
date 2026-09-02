using Opstrax.Api.Services;
using Xunit;

namespace Opstrax.Tests;

// ── Alert → notification delivery spine ─────────────────────────────────────
// Pure-logic tests. No DB required. The pref keys and channel names asserted here
// are the STORED CONTRACT written by the SPA (SettingsPage.tsx NOTIF_CATEGORIES /
// CHANNELS): if MapPrefKey or DefaultFor drifts from what the settings page saves,
// delivery silently stops honoring the user's toggles — these tests pin both sides.

public class AlertNotificationSpineTests
{
    // Every runtime alert_type that ingest can produce must resolve to the pref key
    // the settings page shows a toggle for.
    [Theory]
    [InlineData("speeding", "speed_alert")]
    [InlineData("geofence_breach", "geofence_breach")]
    [InlineData("geofence_exit", "geofence_breach")]
    [InlineData("geofence_enter", "geofence_breach")]
    [InlineData("idling", "idle_alert")]
    [InlineData("sos", "sos_panic")]
    [InlineData("crash", "accident_event")]
    [InlineData("hos_violation", "hos_violation")]
    [InlineData("maintenance_due", "maintenance_due")]
    [InlineData("sla_breach", "sla_breach")]
    [InlineData("fuel_anomaly", "fuel_anomaly")]
    [InlineData("stale_device", "device_offline")]
    [InlineData("SOS", "sos_panic")]              // vocabulary is case-insensitive
    public void MapPrefKey_Covers_Runtime_Alert_Vocabulary(string alertType, string expected)
        => Assert.Equal(expected, AlertNotificationEvents.MapPrefKey(alertType));

    // Harsh-driving events have no settings-page toggle: they feed the safety pipeline,
    // not the notification matrix, so they must NOT map (an unmapped type is skipped).
    [Theory]
    [InlineData("harsh_braking")]
    [InlineData("harsh_acceleration")]
    [InlineData("harsh_cornering")]
    [InlineData("unknown_future_type")]
    public void MapPrefKey_Skips_Unmatrixed_Types(string alertType)
        => Assert.Null(AlertNotificationEvents.MapPrefKey(alertType));

    // Mirror of SettingsPage.tsx buildDefaultNotifPrefs: a user who never saved prefs
    // sees In-App on for everything, Email on for SOS + accident, SMS on for SOS only.
    // Delivery must honor exactly that, or the page's default toggles lie.
    [Theory]
    [InlineData("sos_panic", "Email", true)]
    [InlineData("sos_panic", "SMS", true)]
    [InlineData("sos_panic", "In-App", true)]
    [InlineData("accident_event", "Email", true)]
    [InlineData("accident_event", "SMS", false)]
    [InlineData("speed_alert", "Email", false)]
    [InlineData("speed_alert", "SMS", false)]
    [InlineData("speed_alert", "In-App", true)]
    [InlineData("geofence_breach", "In-App", true)]
    [InlineData("device_offline", "Email", false)]
    public void DefaultFor_Mirrors_SettingsPage_Defaults(string prefKey, string channel, bool expected)
        => Assert.Equal(expected, AlertNotificationEvents.DefaultFor(prefKey, channel));

    // Channel names are the display-cased strings the SPA stores — 'email'/'in_app'
    // would read as a different (always-off) key. Unknown channels must default off.
    [Fact]
    public void DefaultFor_Rejects_Unknown_Channel_Spellings()
    {
        Assert.False(AlertNotificationEvents.DefaultFor("sos_panic", "email"));
        Assert.False(AlertNotificationEvents.DefaultFor("sos_panic", "in_app"));
        Assert.False(AlertNotificationEvents.DefaultFor("sos_panic", "push"));
    }

    [Theory]
    [InlineData("Critical", 1)]
    [InlineData("critical", 1)]
    [InlineData("High", 3)]
    [InlineData("Medium", 5)]
    [InlineData("Warning", 5)]
    public void PriorityFor_Ranks_Severity(string severity, int expected)
        => Assert.Equal(expected, AlertNotificationEvents.PriorityFor(severity));

    [Theory]
    [InlineData("speed_alert", "Speed Alert")]
    [InlineData("sos_panic", "SOS / Panic")]
    [InlineData("accident_event", "Accident / Collision")]
    public void Label_Is_Human_Readable(string prefKey, string expected)
        => Assert.Equal(expected, AlertNotificationEvents.Label(prefKey));
}
