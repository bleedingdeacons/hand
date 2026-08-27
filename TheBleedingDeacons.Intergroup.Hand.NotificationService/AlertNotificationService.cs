using Foundation;
using ObjCRuntime;
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
/// <para><b>Not compiled by anything yet, and iOS is still sent
/// plaintext.</b> The extension needs its own project, an App Group
/// entitlement the app does not have, the payload key moved to a shared
/// keychain, and Apple provisioning — none of it doable without a Mac
/// and a developer account to hand, so <c>FcmTransport</c> encrypts for
/// Android only and leaves the <c>aps</c> path alone. This is kept in
/// step with the format it will one day open, so that when the hardware
/// exists the work is provisioning rather than archaeology.</para>
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
/// <remarks>
/// <para><b>The C# name and the Objective-C name are deliberately
/// different.</b> iOS instantiates this from
/// <c>NSExtensionPrincipalClass</c> in <c>Info.plist</c>, which names the
/// <i>exported</i> class — and that is pinned by the
/// <see cref="RegisterAttribute"/> below, not by what the type is called
/// here. So the native contract reads <c>NotificationService</c> whatever
/// this class is renamed to, and the C# side is free to be named
/// something that is not also the name of its own namespace. It was
/// `NotificationService` in both places, which the Meziantou analyzer
/// refuses (MA0049) and which is genuinely confusing to read.</para>
///
/// <para>Changing the string in the attribute, on the other hand, is a
/// breaking change to the bundle: it must go on matching
/// <c>Info.plist</c> exactly, or iOS launches the extension and finds
/// nothing to instantiate.</para>
/// </remarks>
[Register("NotificationService")]
public sealed class AlertNotificationService : UNNotificationServiceExtension
{
	private Action<UNNotificationContent>? _deliver;
	private UNMutableNotificationContent? _content;

	/// <summary>
	/// The constructor iOS actually uses.
	///
	/// <para><b>Nothing in managed code calls this, and it still has to
	/// exist.</b> The extension is instantiated by the Objective-C
	/// runtime, from the class name in <c>Info.plist</c>'s
	/// <c>NSExtensionPrincipalClass</c> — so what arrives is a native
	/// handle to an object that already exists, and the job of this
	/// constructor is to adopt it rather than to construct anything.
	/// That is the shape every <see cref="Register"/>-ed
	/// <see cref="NSObject"/> subclass takes.</para>
	///
	/// <para>Without it the compiler synthesises a parameterless
	/// constructor whose implicit <c>base()</c> call has nothing to bind
	/// to — <see cref="UNNotificationServiceExtension"/> exposes only
	/// handle-taking constructors — and the class fails to compile with
	/// <c>CS1729</c>. That went unnoticed because nothing compiles this
	/// project: CI builds the Android head alone, and the extension is
	/// unbuilt source until somebody opens it on a Mac.</para>
	///
	/// <para><c>public</c> rather than the customary <c>protected</c>
	/// only because this class is sealed, where a protected member would
	/// be a warning and mean nothing.</para>
	/// </summary>
	public AlertNotificationService(NativeHandle handle)
		: base(handle)
	{
	}

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
			// see it, rather than showing them ciphertext or nothing.
			//
			// Deliberately different from Android, which ignores a push it
			// cannot open and leaves the alert to the poll. iOS has no
			// poll worth the name — a terminated app runs no timer — so
			// dropping this notification would drop the alert, and the
			// alert is the thing that gets someone out of bed.
			_content.Title = UnopenableTitle;
			_content.Body = UnopenableBody;
			contentHandler(_content);
			return;
		}

		// The whole payload is sealed, not just the readable half, so
		// these come out of the decrypted map like everything else.
		_content.Title = Field(opened, "title");
		_content.Body = Field(opened, "body");

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
			_content.Title = UnopenableTitle;
			_content.Body = "This handset could not read the alert in time.";
			_deliver(_content);
		}
	}

	/// <summary>
	/// What the lock screen says when the payload will not open.
	///
	/// <para>Local constants rather than shared with <c>HandAlert</c>,
	/// which no longer has any: the app's Android path shows nothing at
	/// all for this condition, so there is no wording left to share. See
	/// <see cref="DidReceiveNotificationRequest"/> for why the two heads
	/// differ.</para>
	/// </summary>
	private const string UnopenableTitle = "Alert could not be read — sign in again";

	private const string UnopenableBody =
		"This handset could not read the alert. Sign in again to fix it.";

	/// <summary>One field out of the opened payload, or empty.</summary>
	private static string Field(IDictionary<string, string> opened, string key) =>
		opened.TryGetValue(key, out var value) ? value : string.Empty;

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
