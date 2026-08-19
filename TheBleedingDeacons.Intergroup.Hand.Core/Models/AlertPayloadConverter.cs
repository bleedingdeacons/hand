using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TheBleedingDeacons.Intergroup.Hand.Models;

/// <summary>
/// Reads <see cref="HandAlert.Payload"/> from whatever Reach actually put
/// on the wire, rather than only from the shape it means to send.
///
/// <para>The payload is a string map by contract, because the same alert
/// has to survive a round trip through FCM's string-only data block. PHP
/// does not have a distinct empty-map value though, so an alert whose
/// raising plugin added nothing comes back from <c>json_encode</c> as
/// <c>[]</c>, and a plugin that puts a number or a nested structure in
/// sends something that is not a string. The plain
/// <c>Dictionary&lt;string, string&gt;</c> converter throws on all three,
/// and because the alerts arrive as one document, one such alert stops
/// the whole poll — the handset goes quiet with nothing but a warning in
/// the log. For an app whose entire job is to ring, that is the worst
/// possible way to fail, so this converter takes the tolerant reading
/// instead.</para>
///
/// <para>Nothing here is load-bearing for the alarm: the payload holds
/// only whatever the raising plugin added, never the fields Hand rings
/// on. Salvaging what parses and dropping the rest costs nothing the
/// responder can see, where refusing the document costs the alert.</para>
/// </summary>
public sealed class AlertPayloadConverter : JsonConverter<Dictionary<string, string>>
{
	/// <summary>
	/// Called for <c>null</c> as well, so an explicit null payload lands
	/// as an empty map rather than leaving the property null underneath a
	/// non-nullable declaration.
	/// </summary>
	public override bool HandleNull => true;

	public override Dictionary<string, string> Read(
		ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		var payload = new Dictionary<string, string>(StringComparer.Ordinal);

		if (reader.TokenType != JsonTokenType.StartObject)
		{
			// `[]`, `""`, `null` — PHP's empty map and its neighbours. Not a
			// map, but not an error either: there is nothing to read.
			// Anything with contents is skipped whole; the reader has to be
			// left on the value's last token either way.
			reader.Skip();
			return payload;
		}

		while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
		{
			var key = reader.GetString()!;
			reader.Read();
			payload[key] = ReadValue(ref reader);
		}

		return payload;
	}

	public override void Write(
		Utf8JsonWriter writer, Dictionary<string, string> value, JsonSerializerOptions options)
	{
		ArgumentNullException.ThrowIfNull(writer);
		ArgumentNullException.ThrowIfNull(value);

		writer.WriteStartObject();

		foreach (var pair in value)
		{
			writer.WriteString(pair.Key, pair.Value);
		}

		writer.WriteEndObject();
	}

	/// <summary>
	/// Flatten one value to text. Numbers and booleans keep their source
	/// spelling, which is what they would have arrived as over FCM
	/// anyway; objects and arrays keep their raw JSON, so a consumer that
	/// understands them still can and one that does not is merely holding
	/// a string it will not recognise.
	/// </summary>
	private static string ReadValue(ref Utf8JsonReader reader)
	{
		switch (reader.TokenType)
		{
			case JsonTokenType.String:
				return reader.GetString() ?? string.Empty;

			case JsonTokenType.Null:
				return string.Empty;

			case JsonTokenType.True:
				return "true";

			case JsonTokenType.False:
				return "false";

			case JsonTokenType.Number:
				// The source spelling, not a reparsed one: round-tripping
				// through double would turn a large id into 1.23E+18.
				return Encoding.UTF8.GetString(
					reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan);

			default:
				using (var document = JsonDocument.ParseValue(ref reader))
				{
					return document.RootElement.GetRawText();
				}
		}
	}
}
