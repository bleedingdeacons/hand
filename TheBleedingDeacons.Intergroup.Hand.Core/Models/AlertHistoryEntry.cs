using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TheBleedingDeacons.Intergroup.Hand.Models;

/// <summary>
/// One alert, as it is remembered after the fact.
///
/// <para><b>A record of what happened, not a copy of the alert.</b> An
/// alert exists to be acted on and then to go away; this exists to answer
/// "what came in last night, and what became of it" — which is a question
/// a responder gets asked at an intergroup meeting, and which the app
/// previously could not answer at all because nothing survived the
/// process.</para>
///
/// <para><b>It holds no contact details, and must not learn to.</b> The
/// caller's name and number are fetched on demand, audited by Reach, and
/// never stored on the handset — see <see cref="HandAlert.Contact"/>.
/// Writing them into a file that outlives the alert would undo that
/// quietly and permanently. The subject, body and reference are the same
/// text that already reached the lock screen, so keeping those adds no
/// exposure the alert did not already carry.</para>
/// </summary>
public partial class AlertHistoryEntry : ObservableObject
{
	/// <summary>The alert's own id, and this entry's identity.</summary>
	[JsonPropertyName("id")]
	public long Id { get; set; }

	/// <summary>
	/// The send this alert belonged to. Kept because an acknowledgement
	/// notice names the message rather than the alert, and it is what
	/// lets "somebody else answered" find the row it is about.
	/// </summary>
	[JsonPropertyName("message_uuid")]
	public string MessageUuid { get; set; } = string.Empty;

	/// <summary>What the alert said first. The list shows this.</summary>
	[JsonPropertyName("subject")]
	public string Subject { get; set; } = string.Empty;

	/// <summary>The longer text, revealed when the row is opened.</summary>
	[JsonPropertyName("body")]
	public string Body { get; set; } = string.Empty;

	/// <summary>The raiser's own reference, shown with the body.</summary>
	[JsonPropertyName("reference")]
	public string Reference { get; set; } = string.Empty;

	/// <summary>
	/// The level it arrived at: red, yellow or blue. Shown as the row's
	/// colour, so a night's history reads as a shape before it is read as
	/// a list.
	/// </summary>
	[JsonPropertyName("level")]
	public string Level { get; set; } = HandAlert.LevelYellow;

	/// <summary>What became of it. One of <see cref="AlertHistoryStatus"/>.</summary>
	[JsonPropertyName("status")]
	public string Status { get; set; } = AlertHistoryStatus.Outstanding;

	/// <summary>
	/// Who answered, when somebody else did. Empty otherwise. A Unity
	/// anonymous name, never an address — it comes from the notice, which
	/// carries no more than that.
	/// </summary>
	[JsonPropertyName("answered_by")]
	public string AnsweredBy { get; set; } = string.Empty;

	/// <summary>When it arrived here, as a Unix timestamp.</summary>
	[JsonPropertyName("received_at")]
	public long ReceivedAt { get; set; }

	/// <summary>
	/// When it stopped being outstanding, or 0 while it still is.
	/// </summary>
	[JsonPropertyName("settled_at")]
	public long SettledAt { get; set; }

	/// <summary>
	/// Whether the row is open in the list.
	///
	/// <para>View state, so it is not stored: a history that reopened
	/// yesterday's rows on every launch would be a list nobody could
	/// skim. Every row starts closed.</para>
	/// </summary>
	[JsonIgnore]
	[ObservableProperty]
	public partial bool IsExpanded { get; set; }

	/// <summary>The time this arrived, as the list shows it.</summary>
	[JsonIgnore]
	public string ReceivedLine =>
		DateTimeOffset.FromUnixTimeSeconds(ReceivedAt).ToLocalTime().ToString("ddd d MMM, HH:mm");

	/// <summary>
	/// The status as a sentence. "Answered" is the only one that names
	/// anybody, and only because a row saying somebody else dealt with it
	/// is uninformative without saying who.
	/// </summary>
	[JsonIgnore]
	public string StatusLine => Status switch
	{
		AlertHistoryStatus.Acknowledged => "Acknowledged by you",
		AlertHistoryStatus.Closed => "Closed",
		AlertHistoryStatus.Expired => "Expired unanswered",
		AlertHistoryStatus.Answered =>
			AnsweredBy.Length > 0 ? $"Answered by {AnsweredBy}" : "Answered by another responder",
		_ => "Outstanding",
	};

	/// <summary>
	/// The row's colour, from the level. Deliberately the same three
	/// values <see cref="HandAlert.LevelBackground"/> uses: a red alert
	/// should look like the same thing in the history as it did on the
	/// night.
	/// </summary>
	[JsonIgnore]
	public string LevelBackground => Level switch
	{
		HandAlert.LevelRed => "#B3261E",
		HandAlert.LevelBlue => "#1565C0",
		_ => "#F9A825",
	};

	/// <summary>Matches <see cref="HandAlert.LevelForeground"/>.</summary>
	[JsonIgnore]
	public string LevelForeground => "#FFFFFF";

	/// <summary>Whether there is anything to reveal by opening the row.</summary>
	[JsonIgnore]
	public bool HasDetail => Body.Length > 0 || Reference.Length > 0;

	/// <summary>Build an entry from an alert as it arrives.</summary>
	public static AlertHistoryEntry From(HandAlert alert, long receivedAt)
	{
		ArgumentNullException.ThrowIfNull(alert);

		return new AlertHistoryEntry
		{
			Id = alert.Id,
			MessageUuid = alert.MessageUuid,
			Subject = alert.Title,
			Body = alert.Body,
			Reference = alert.Reference,
			Level = alert.LevelOrDerived,
			// An informational alert was never anybody's to take on, so it
			// is not "outstanding" in the sense the list means — nothing
			// is waiting on a responder.
			Status = alert.IsInformational
				? AlertHistoryStatus.Closed
				: AlertHistoryStatus.Outstanding,
			ReceivedAt = receivedAt,
		};
	}
}

/// <summary>
/// What became of an alert. Strings rather than an enum because they are
/// written to a file that outlives the build that wrote it, and a
/// renumbered enum would silently reinterpret every stored row.
/// </summary>
public static class AlertHistoryStatus
{
	/// <summary>Arrived, and nobody has dealt with it yet.</summary>
	public const string Outstanding = "outstanding";

	/// <summary>This handset took it on.</summary>
	public const string Acknowledged = "acknowledged";

	/// <summary>Another responder took it, and this handset was told.</summary>
	public const string Answered = "answered";

	/// <summary>Read and closed here; nobody had to take it on.</summary>
	public const string Closed = "closed";

	/// <summary>Its window shut with nothing done about it.</summary>
	public const string Expired = "expired";
}
