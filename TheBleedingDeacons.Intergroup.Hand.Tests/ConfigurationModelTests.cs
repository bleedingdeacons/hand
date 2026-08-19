using System.Text.Json;
using TheBleedingDeacons.Intergroup.Hand.Models;
using Xunit;

namespace TheBleedingDeacons.Intergroup.Hand.Tests;

/// <summary>
/// <see cref="ReachConfiguration"/>: where the server is and how often to
/// ask it.
/// </summary>
public sealed class ReachConfigurationTests
{
	[Theory]
	[InlineData("https://aa-bristol.org/", true)]
	[InlineData("http://localhost:8080/", true)]
	[InlineData("https://aa-bristol.org/wp", true)]
	[InlineData("", false)]
	[InlineData("   ", false)]
	[InlineData("aa-bristol.org", false)]
	[InlineData("ftp://aa-bristol.org/", false)]
	[InlineData("hand://auth", false)]
	public void IsValid_AcceptsOnlyAnAbsoluteHttpUrl(string baseUrl, bool expected) =>
		Assert.Equal(expected, new ReachConfiguration { BaseUrl = baseUrl }.IsValid());

	/// <summary>
	/// The trailing slash is not cosmetic. <c>new Uri(base, relative)</c>
	/// discards the last path segment of a base that does not end in one,
	/// so a WordPress install in a subdirectory would silently lose it.
	/// </summary>
	[Fact]
	public void Normalised_AddsTheTrailingSlashThatSubdirectoryInstallsDependOn()
	{
		var normalised = new ReachConfiguration { BaseUrl = "https://aa-bristol.org/wp" }.Normalised();

		Assert.Equal("https://aa-bristol.org/wp/", normalised.BaseUrl);
		Assert.Equal(
			"https://aa-bristol.org/wp/wp-json/reach/v1/alerts",
			new Uri(new Uri(normalised.BaseUrl), "wp-json/reach/v1/alerts").ToString());
	}

	[Fact]
	public void Normalised_LeavesAnExistingTrailingSlashAlone() =>
		Assert.Equal(
			"https://aa-bristol.org/",
			new ReachConfiguration { BaseUrl = "https://aa-bristol.org/" }.Normalised().BaseUrl);

	[Fact]
	public void Normalised_TrimsWhitespace() =>
		Assert.Equal(
			"https://aa-bristol.org/",
			new ReachConfiguration { BaseUrl = "  https://aa-bristol.org/  " }.Normalised().BaseUrl);

	/// <summary>An empty address stays empty — that is "not configured".</summary>
	[Fact]
	public void Normalised_DoesNotTurnAnEmptyAddressIntoASlash() =>
		Assert.Equal(string.Empty, new ReachConfiguration { BaseUrl = "  " }.Normalised().BaseUrl);

	/// <summary>
	/// A mistyped interval is a handset that either hammers the server or
	/// misses its shift, so it is clamped rather than trusted.
	/// </summary>
	[Theory]
	[InlineData(0, 5)]
	[InlineData(-30, 5)]
	[InlineData(4, 5)]
	[InlineData(5, 5)]
	[InlineData(20, 20)]
	[InlineData(300, 300)]
	[InlineData(86400, 300)]
	public void Normalised_ClampsThePollInterval(int given, int expected) =>
		Assert.Equal(expected, new ReachConfiguration { PollSeconds = given }.Normalised().PollSeconds);

	[Fact]
	public void Normalised_CarriesOnDutyThrough()
	{
		Assert.False(new ReachConfiguration { OnDuty = false }.Normalised().OnDuty);
		Assert.True(new ReachConfiguration { OnDuty = true }.Normalised().OnDuty);
	}

	[Fact]
	public void Normalised_ReturnsACopy()
	{
		var original = new ReachConfiguration { BaseUrl = "https://aa-bristol.org", PollSeconds = 1 };

		var normalised = original.Normalised();

		Assert.NotSame(original, normalised);
		Assert.Equal("https://aa-bristol.org", original.BaseUrl);
		Assert.Equal(1, original.PollSeconds);
	}

	[Fact]
	public void DefaultsToTwentySecondsOnDuty()
	{
		var configuration = new ReachConfiguration();

		Assert.Equal(20, configuration.PollSeconds);
		Assert.True(configuration.OnDuty);
	}
}

/// <summary>
/// <see cref="BetterStackConfiguration"/>: the log shipping settings, and
/// the endpoint normalisation that Register learned the hard way.
/// </summary>
public sealed class BetterStackConfigurationTests
{
	/// <summary>
	/// Better Stack's dashboard shows the ingest address as a bare
	/// hostname, so that is what gets pasted in. Without the scheme,
	/// <c>Uri.TryCreate</c> refuses it, the configuration reads as
	/// invalid, and the app silently ships no logs at all.
	/// </summary>
	[Theory]
	[InlineData("s123456.eu-central-1a.betterstackdata.com", "https://s123456.eu-central-1a.betterstackdata.com")]
	[InlineData("  s123456.betterstackdata.com  ", "https://s123456.betterstackdata.com")]
	[InlineData("https://s123456.betterstackdata.com", "https://s123456.betterstackdata.com")]
	[InlineData("http://localhost:9000", "http://localhost:9000")]
	public void Endpoint_GetsTheSchemeItNeedsToParse(string given, string expected) =>
		Assert.Equal(expected, new BetterStackConfiguration { Endpoint = given }.Endpoint);

	/// <summary>
	/// Empty means "not configured", which is a supported state. It must
	/// not become a bare "https://".
	/// </summary>
	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData(null)]
	public void Endpoint_StaysEmptyWhenItIsNotSet(string? given) =>
		Assert.Equal(string.Empty, new BetterStackConfiguration { Endpoint = given! }.Endpoint);

	[Fact]
	public void IsValid_NeedsBothATokenAndAnEndpoint()
	{
		Assert.True(new BetterStackConfiguration { SourceToken = "t", Endpoint = "s1.betterstackdata.com" }.IsValid());
		Assert.False(new BetterStackConfiguration { SourceToken = "", Endpoint = "s1.betterstackdata.com" }.IsValid());
		Assert.False(new BetterStackConfiguration { SourceToken = "   ", Endpoint = "s1.betterstackdata.com" }.IsValid());
		Assert.False(new BetterStackConfiguration { SourceToken = "t", Endpoint = "" }.IsValid());
	}

	/// <summary>A scheme we do not recognise is left alone, and refused.</summary>
	[Fact]
	public void IsValid_RefusesANonHttpScheme() =>
		Assert.False(new BetterStackConfiguration { SourceToken = "t", Endpoint = "ftp://logs.example" }.IsValid());

	[Fact]
	public void ToLogSafe_MasksTheTokenAndKeepsTheEndpoint()
	{
		var safe = new BetterStackConfiguration
		{
			SourceToken = "a-real-secret",
			Endpoint = "s1.betterstackdata.com",
		}.ToLogSafe();

		Assert.Equal("***", safe.SourceToken);
		Assert.Equal("https://s1.betterstackdata.com", safe.Endpoint);
	}

	[Fact]
	public void ToLogSafe_DoesNotInventAMaskForAnAbsentToken() =>
		Assert.Equal(string.Empty, new BetterStackConfiguration().ToLogSafe().SourceToken);
}

/// <summary>
/// <see cref="DeviceSession"/> and <see cref="ReachResult{T}"/> — what
/// Reach says about this handset, and how the answer is carried.
/// </summary>
public sealed class SessionTests
{
	[Fact]
	public void DeviceSession_ReadsReachsJson()
	{
		const string json = """
			{
			  "token": "abc123",
			  "device_id": 9,
			  "responder": "Dave B.",
			  "platform": "android",
			  "push_provider": "fcm",
			  "label": "Duty phone",
			  "authorised": true
			}
			""";

		var session = JsonSerializer.Deserialize<DeviceSession>(json);

		Assert.NotNull(session);
		Assert.Equal("abc123", session.Token);
		Assert.Equal(9, session.DeviceId);
		Assert.Equal("Dave B.", session.Responder);
		Assert.Equal("android", session.Platform);
		Assert.Equal("fcm", session.PushProvider);
		Assert.Equal("Duty phone", session.Label);
		Assert.True(session.Authorised);
	}

	/// <summary>
	/// The session check returns no token — Reach cannot reissue one — so
	/// every field has to survive being absent.
	/// </summary>
	[Fact]
	public void DeviceSession_DefaultsEveryStringToEmptyRatherThanNull()
	{
		var session = JsonSerializer.Deserialize<DeviceSession>("""{"device_id":9}""");

		Assert.NotNull(session);
		Assert.Equal(string.Empty, session.Token);
		Assert.Equal(string.Empty, session.Responder);
		Assert.False(session.Authorised);
	}

	[Fact]
	public void ReachResult_Ok_CarriesTheValueAndNoFailure()
	{
		var result = ReachResult<int>.Ok(42);

		Assert.True(result.Success);
		Assert.Equal(42, result.Value);
		Assert.Equal(ReachFailure.None, result.Failure);
		Assert.Equal(string.Empty, result.Message);
	}

	[Fact]
	public void ReachResult_Fail_CarriesTheReasonAndNoValue()
	{
		var result = ReachResult<DeviceSession>.Fail(ReachFailure.NotEligible, "Certification lapsed.");

		Assert.False(result.Success);
		Assert.Null(result.Value);
		Assert.Equal(ReachFailure.NotEligible, result.Failure);
		Assert.Equal("Certification lapsed.", result.Message);
	}
}
