using System.Text.Json;
using TheBleedingDeacons.Intergroup.Hand.Models;
using Xunit;

namespace TheBleedingDeacons.Intergroup.Hand.Tests;

/// <summary>
/// The alert as it arrives, by both routes.
///
/// <para>The push route gets the closer look, because it is the one where
/// every value has been flattened to a string on the way through FCM and
/// has to survive being parsed back. A field that comes back wrong there
/// is a handset that rings about the wrong thing, or does not ring.</para>
/// </summary>
public sealed class HandAlertTests
{
	[Fact]
	public void FromPushData_ReadsEveryField()
	{
		var alert = HandAlert.FromPushData(new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["alert_id"] = "4242",
			["kind"] = "shift_uncovered",
			["source"] = "trusted",
			["priority"] = "urgent",
			["title"] = "Shift uncovered",
			["body"] = "Nobody is on the helpline.",
			["reference"] = "SHIFT-2026-08-15-N",
			["created_at"] = "1755250000",
			["expires_at"] = "1755253600",
			["has_contact"] = "1",
		});

		Assert.NotNull(alert);
		Assert.Equal(4242, alert.Id);
		Assert.Equal("shift_uncovered", alert.Kind);
		Assert.Equal("trusted", alert.Source);
		Assert.Equal("urgent", alert.Priority);
		Assert.Equal("Shift uncovered", alert.Title);
		Assert.Equal("Nobody is on the helpline.", alert.Body);
		Assert.Equal("SHIFT-2026-08-15-N", alert.Reference);
		Assert.Equal(1755250000, alert.CreatedAt);
		Assert.Equal(1755253600, alert.ExpiresAt);
		Assert.True(alert.HasContact);
		Assert.True(alert.IsUrgent);
	}

	/// <summary>
	/// The id is what an acknowledgement is keyed on, so a message without
	/// a usable one has to be refused outright — an alert that cannot be
	/// acknowledged would ring until the battery went.
	/// </summary>
	[Theory]
	[InlineData("")]
	[InlineData("not-a-number")]
	[InlineData("0")]
	[InlineData("-1")]
	[InlineData("1.5")]
	public void FromPushData_RefusesUnusableId(string rawId)
	{
		var alert = HandAlert.FromPushData(new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["alert_id"] = rawId,
			["title"] = "Shift uncovered",
		});

		Assert.Null(alert);
	}

	[Fact]
	public void FromPushData_RefusesAMessageWithNoIdAtAll() =>
		Assert.Null(HandAlert.FromPushData(new Dictionary<string, string>(StringComparer.Ordinal)));

	[Fact]
	public void FromPushData_RejectsNull() =>
		Assert.Throws<ArgumentNullException>(() => HandAlert.FromPushData(null!));

	[Fact]
	public void FromPushData_DefaultsEveryFieldItWasNotSent()
	{
		var alert = HandAlert.FromPushData(new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["alert_id"] = "7",
		});

		Assert.NotNull(alert);
		Assert.Equal(string.Empty, alert.Kind);
		Assert.Equal(string.Empty, alert.Title);
		Assert.Equal(string.Empty, alert.Reference);
		Assert.Equal(0, alert.CreatedAt);
		Assert.Equal(0, alert.ExpiresAt);
		Assert.False(alert.HasContact);
		Assert.Empty(alert.Payload);
	}

	/// <summary>
	/// A timestamp that will not parse must not become a plausible one.
	/// Zero reads as "no expiry", which is the safe way to be wrong: the
	/// alert rings rather than being silently discarded as stale.
	/// </summary>
	[Fact]
	public void FromPushData_TreatsAnUnparseableTimestampAsAbsent()
	{
		var alert = HandAlert.FromPushData(new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["alert_id"] = "7",
			["expires_at"] = "soon",
		});

		Assert.NotNull(alert);
		Assert.Equal(0, alert.ExpiresAt);
		Assert.False(alert.IsExpired(DateTimeOffset.UtcNow));
	}

	[Theory]
	[InlineData("1", true)]
	[InlineData("true", true)]
	[InlineData("0", false)]
	[InlineData("false", false)]
	[InlineData("yes", false)]
	[InlineData("", false)]
	public void FromPushData_ReadsHasContactAsTheServerSpellsIt(string raw, bool expected)
	{
		var alert = HandAlert.FromPushData(new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["alert_id"] = "7",
			["has_contact"] = raw,
		});

		Assert.NotNull(alert);
		Assert.Equal(expected, alert.HasContact);
	}

	/// <summary>
	/// The raising plugin's own extras come through, but the transport's
	/// own keys must not reappear as payload entries — a plugin's "title"
	/// would otherwise show up twice, once as the alert's and once as data.
	/// </summary>
	[Fact]
	public void FromPushData_KeepsPluginExtrasAndDropsReservedKeys()
	{
		var alert = HandAlert.FromPushData(new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["alert_id"] = "7",
			["title"] = "Shift uncovered",
			["channel"] = "reach_alerts",
			["sound"] = "reach_alert",
			["rota_slot"] = "night",
			["region"] = "bristol",
		});

		Assert.NotNull(alert);
		Assert.Equal(
			["region", "rota_slot"],
			alert.Payload.Keys.Order(StringComparer.Ordinal));
		Assert.Equal("night", alert.Payload["rota_slot"]);
	}

	[Fact]
	public void IsExpired_IsFalseWhenThereIsNoExpiry() =>
		Assert.False(Alerts.New(expiresAt: 0).IsExpired(DateTimeOffset.UtcNow));

	[Fact]
	public void IsExpired_IsTrueOnTheSecondItExpires()
	{
		var now = DateTimeOffset.FromUnixTimeSeconds(1_755_250_000);
		var alert = Alerts.New(expiresAt: 1_755_250_000);

		Assert.True(alert.IsExpired(now));
		Assert.False(alert.IsExpired(now.AddSeconds(-1)));
	}

	[Theory]
	[InlineData("urgent", true)]
	[InlineData("URGENT", true)]
	[InlineData("Urgent", true)]
	[InlineData("normal", false)]
	[InlineData("", false)]
	public void IsUrgent_IgnoresCase(string priority, bool expected) =>
		Assert.Equal(expected, new HandAlert { Priority = priority }.IsUrgent);

	/// <summary>
	/// The removal notice is an instruction, not an alert, and the spelling
	/// is a wire contract with Reach — see the constant's own note.
	/// </summary>
	[Theory]
	[InlineData("device_removed", true)]
	[InlineData("DEVICE_REMOVED", true)]
	[InlineData("device removed", false)]
	[InlineData("shift_uncovered", false)]
	public void IsDeviceRemoval_MatchesTheWireSpellingOnly(string kind, bool expected) =>
		Assert.Equal(expected, new HandAlert { Kind = kind }.IsDeviceRemoval);

	/// <summary>
	/// The contact is fetched later and shown when it arrives, so the flag
	/// the page binds to has to change with it.
	/// </summary>
	[Fact]
	public void Contact_RaisesIsContactShown()
	{
		var alert = Alerts.New();
		var changed = new List<string?>();
		alert.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

		Assert.False(alert.IsContactShown);

		alert.Contact = "07700 900000";

		Assert.True(alert.IsContactShown);
		Assert.Contains(nameof(HandAlert.Contact), changed, StringComparer.Ordinal);
		Assert.Contains(nameof(HandAlert.IsContactShown), changed, StringComparer.Ordinal);
	}

	[Fact]
	public void IsLoadingContact_NotifiesTheView()
	{
		var alert = Alerts.New();
		var changed = new List<string?>();
		alert.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

		alert.IsLoadingContact = true;

		Assert.Contains(nameof(HandAlert.IsLoadingContact), changed, StringComparer.Ordinal);
	}

	/// <summary>The poll route: the same alert, parsed from Reach's JSON.</summary>
	[Fact]
	public void Deserialises_FromReachJson()
	{
		const string json = """
			{
			  "id": 12,
			  "kind": "shift_uncovered",
			  "source": "trusted",
			  "priority": "urgent",
			  "title": "Shift uncovered",
			  "body": "Nobody is on the helpline.",
			  "reference": "SHIFT-2026-08-15-N",
			  "created_at": 1755250000,
			  "expires_at": 1755253600,
			  "has_contact": true,
			  "payload": { "rota_slot": "night" }
			}
			""";

		var alert = JsonSerializer.Deserialize<HandAlert>(json);

		Assert.NotNull(alert);
		Assert.Equal(12, alert.Id);
		Assert.True(alert.IsUrgent);
		Assert.True(alert.HasContact);
		Assert.Equal("night", alert.Payload["rota_slot"]);
	}

	/// <summary>
	/// Contact and its loading flag are view state fetched separately, and
	/// must never be written into anything that goes over the wire.
	/// </summary>
	[Fact]
	public void Serialises_WithoutTheContactDetails()
	{
		var alert = Alerts.New();
		alert.Contact = "07700 900000";
		alert.IsLoadingContact = true;

		var json = JsonSerializer.Serialize(alert);

		Assert.DoesNotContain("07700", json, StringComparison.Ordinal);
		Assert.DoesNotContain("isLoadingContact", json, StringComparison.OrdinalIgnoreCase);
	}
}
