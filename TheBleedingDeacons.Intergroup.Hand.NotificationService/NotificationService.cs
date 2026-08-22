using Foundation;
using TheBleedingDeacons.Intergroup.Hand.Models;
using UserNotifications;

namespace TheBleedingDeacons.Intergroup.Hand.NotificationService;

/// <summary>
/// Opens an encrypted alert before iOS puts it on the lock screen.
///
/// <para><b>Why this exists at all.</b> Android hands a data-only push
/// to Hand's own messaging service, which decrypts and builds the
/// notification itself — no extension needed, which is why Android
/// shipped first. iOS renders the lock screen from the <c>aps</c>
/// dictionary before the app is consulted, so without something running
/// in between, an encrypted alert would put base64 in front of whoever
/// is standing near the phone. This is that something.</para>
///
/// <para>iOS launches it for any push carrying <c>mutable-content: 1</c>,
/// gives it roughly thirty seconds, and shows whatever it hands back —
/// or the original payload if it runs out of time. So the work here is
/// deliberately local: read a key from the keychain, decrypt, swap two
/// strings. No network, nothing that can hang.</para>
///
/// <para><b>Every failure shows the alert rather than hiding it.</b> A
/// missing key, a payload that will not open, an unexpected shape — all
/// of them fall through to <see cref="ServiceExtensionTimeWillExpire"/>'s
/// behaviour of delivering what arrived. A responder woken by an alert
/// they cannot read will phone in; one never woken will not. That is the
/// same judgement the Android path and the server both make.</para>
/// </summary>
[Register("NotificationService")]
public sealed class NotificationService : UNNotificationServiceExtension
{
	private Action<UNNotificationContent>? _deliver;
	private UNMutableNotificationContent? _content;

	/// <summary>
	/// Called by iOS with the arriving notification. Whatever is passed
	/// to <paramref name="contentHandler"/> is what the responder sees.
	/// </summary>
	public override void DidReceiveNotificationRequest(
		UNNotificationRequest request,
		Action<UNNotificationContent> contentHandler)
	{
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(contentHandler);

		_deliver = contentHandler;
		_content = request.Content?.MutableCopy() as UNMutableNotificationContent;

		if (_content is null)
		{
			// Nothing to rewrite into. Hand back what arrived.
			contentHandler(request.Content!);
			return;
		}

		var sealedPayload = StringFrom(_content.UserInfo, "ciphertext");

		if (sealedPayload.Length == 0)
		{
			// Not an encrypted alert. Either an older server, or one of
			// the notices Reach sends in the clear on purpose.
			contentHandler(_content);
			return;
		}

		var opened = AlertPayloadCipher.Open(sealedPayload, SharedKeychain.Read());

		if (opened is null)
		{
			// The key is missing or wrong. Say so where the responder will
			// see it, rather than showing them ciphertext or nothing —
			// the same wording the app uses for the same condition.
			_content.Title = HandAlert.UnopenableMessage;
			_content.Body = "This handset could not read the alert. Sign in again to fix it.";
			contentHandler(_content);
			return;
		}

		_content.Title = opened.Title;
		_content.Body = opened.Body;

		contentHandler(_content);
	}

	/// <summary>
	/// iOS is about to give up on us. Deliver the best we have.
	///
	/// <para>Not reachable in practice — decryption is a keychain read and
	/// an AES-GCM open, both microseconds, and nothing here touches the
	/// network. It is implemented because the alternative when it *is*
	/// called is iOS showing the raw payload, and the raw payload of an
	/// encrypted alert is base64.</para>
	/// </summary>
	public override void TimeWillExpire()
	{
		if (_deliver is null)
		{
			return;
		}

		if (_content is not null)
		{
			_content.Title = HandAlert.UnopenableMessage;
			_content.Body = "This handset could not read the alert in time.";
			_deliver(_content);
		}
	}

	/// <summary>
	/// One string out of the userInfo dictionary, or empty.
	///
	/// <para>Everything in an FCM data message arrives as a string, but
	/// the dictionary is typed as object-to-object and a push is not
	/// something to trust the shape of — this runs before anything else
	/// has validated it.</para>
	/// </summary>
	private static string StringFrom(NSDictionary userInfo, string key)
	{
		if (userInfo is null)
		{
			return string.Empty;
		}

		using var nsKey = new NSString(key);

		return userInfo.TryGetValue(nsKey, out var value) && value is NSString text
			? text.ToString()
			: string.Empty;
	}
}
