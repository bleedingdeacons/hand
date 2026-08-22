using System.Text.Json.Serialization;

namespace TheBleedingDeacons.Intergroup.Hand.Models;

/// <summary>
/// The readable half of an alert, as it travels encrypted.
///
/// <para>These three fields and no others: they are the ones a person
/// could read off a lock screen, so they are the ones Reach seals. The
/// property names match the JSON Reach packs inside the ciphertext, and
/// changing either side without the other is a handset that decrypts
/// successfully into a blank alert.</para>
/// </summary>
public sealed class AlertText
{
	[JsonPropertyName("title")]
	public string Title { get; set; } = string.Empty;

	[JsonPropertyName("body")]
	public string Body { get; set; } = string.Empty;

	[JsonPropertyName("reference")]
	public string Reference { get; set; } = string.Empty;
}
