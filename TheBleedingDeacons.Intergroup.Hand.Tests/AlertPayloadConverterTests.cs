using System.Text.Json;
using TheBleedingDeacons.Intergroup.Hand.Models;
using Xunit;

namespace TheBleedingDeacons.Intergroup.Hand.Tests;

/// <summary>
/// The tolerant payload reader.
///
/// <para>Every case here is one that the stock dictionary converter throws
/// on, and every throw costs the whole poll document rather than the one
/// alert — which for an app whose job is to ring is the worst available
/// way to fail. So the shape of these tests is: odd input in, no
/// exception out, and as much of the payload salvaged as was meaningful.</para>
/// </summary>
public sealed class AlertPayloadConverterTests
{
	private static Dictionary<string, string> Read(string payloadJson)
	{
		var alert = JsonSerializer.Deserialize<HandAlert>($$"""{"id":1,"payload":{{payloadJson}}}""");
		Assert.NotNull(alert);
		return alert.Payload;
	}

	[Fact]
	public void ReadsAPlainStringMap()
	{
		var payload = Read("""{"rota_slot":"night","region":"bristol"}""");

		Assert.Equal(2, payload.Count);
		Assert.Equal("night", payload["rota_slot"]);
		Assert.Equal("bristol", payload["region"]);
	}

	/// <summary>
	/// PHP has no distinct empty-map value, so an alert whose raising
	/// plugin added nothing comes back from <c>json_encode</c> as <c>[]</c>.
	/// </summary>
	[Theory]
	[InlineData("[]")]
	[InlineData("null")]
	[InlineData("\"\"")]
	[InlineData("\"not a map at all\"")]
	[InlineData("0")]
	[InlineData("false")]
	[InlineData("[1,2,3]")]
	public void TreatsANonMapAsAnEmptyPayload(string payloadJson) =>
		Assert.Empty(Read(payloadJson));

	/// <summary>
	/// The reader has to be left on the value's last token whatever it
	/// skipped, or the rest of the document is misread. Checking a field
	/// that comes <i>after</i> the payload is what proves it.
	/// </summary>
	[Fact]
	public void LeavesTheReaderInPlaceAfterSkippingANonMap()
	{
		var alert = JsonSerializer.Deserialize<HandAlert>(
			"""{"id":1,"payload":[{"a":1},{"b":2}],"reference":"SHIFT-9"}""");

		Assert.NotNull(alert);
		Assert.Empty(alert.Payload);
		Assert.Equal("SHIFT-9", alert.Reference);
	}

	/// <summary>
	/// A number keeps its source spelling. Reparsing through a double
	/// would turn a large id into <c>1.23E+18</c>, which is not the value
	/// the server sent.
	/// </summary>
	[Fact]
	public void KeepsANumbersSourceSpelling()
	{
		var payload = Read("""{"small":42,"big":1234567890123456789,"fraction":1.50,"negative":-7,"exponent":1e3}""");

		Assert.Equal("42", payload["small"]);
		Assert.Equal("1234567890123456789", payload["big"]);
		Assert.Equal("1.50", payload["fraction"]);
		Assert.Equal("-7", payload["negative"]);
		Assert.Equal("1e3", payload["exponent"]);
	}

	[Fact]
	public void FlattensBooleansAndNulls()
	{
		var payload = Read("""{"yes":true,"no":false,"nothing":null}""");

		Assert.Equal("true", payload["yes"]);
		Assert.Equal("false", payload["no"]);
		Assert.Equal(string.Empty, payload["nothing"]);
	}

	/// <summary>
	/// Nested structures keep their raw JSON, so a consumer that
	/// understands them still can and one that does not is merely holding
	/// a string it will not recognise.
	/// </summary>
	[Fact]
	public void KeepsNestedStructuresAsRawJson()
	{
		var payload = Read("""{"shift":{"slot":"night","cover":2},"tags":["a","b"]}""");

		Assert.Equal("""{"slot":"night","cover":2}""", payload["shift"]);
		Assert.Equal("""["a","b"]""", payload["tags"]);
	}

	[Fact]
	public void ReadsAnEmptyMap() => Assert.Empty(Read("{}"));

	/// <summary>
	/// An explicit null payload must land as an empty map, not leave the
	/// property null underneath a non-nullable declaration.
	/// </summary>
	[Fact]
	public void NeverLeavesThePayloadNull()
	{
		var alert = JsonSerializer.Deserialize<HandAlert>("""{"id":1,"payload":null}""");

		Assert.NotNull(alert);
		Assert.NotNull(alert.Payload);
	}

	[Fact]
	public void RoundTripsThroughWrite()
	{
		var alert = Alerts.New();
		alert.Payload["rota_slot"] = "night";
		alert.Payload["cover"] = "2";

		var round = JsonSerializer.Deserialize<HandAlert>(JsonSerializer.Serialize(alert));

		Assert.NotNull(round);
		Assert.Equal("night", round.Payload["rota_slot"]);
		Assert.Equal("2", round.Payload["cover"]);
	}

	[Fact]
	public void WriteRejectsNulls()
	{
		var converter = new AlertPayloadConverter();
		var buffer = new System.IO.MemoryStream();
		using var writer = new Utf8JsonWriter(buffer);

		Assert.Throws<ArgumentNullException>(() =>
			converter.Write(writer, null!, JsonSerializerOptions.Default));
		Assert.Throws<ArgumentNullException>(() =>
			converter.Write(null!, [], JsonSerializerOptions.Default));
	}

	[Fact]
	public void HandlesNullSoAnExplicitNullReachesTheReader() =>
		Assert.True(new AlertPayloadConverter().HandleNull);
}
