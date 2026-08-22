using Foundation;
using Security;

namespace TheBleedingDeacons.Intergroup.Hand.NotificationService;

/// <summary>
/// The one keychain entry the app and its notification extension both
/// reach: the key alert payloads are encrypted to.
///
/// <para><b>Why not SecureStorage.</b> Everywhere else in Hand the
/// payload key goes through MAUI's <c>SecureStorage</c>, which is the
/// right thing and needs no help. It cannot be used here. An extension
/// is a separate process with its own bundle identifier, so it gets its
/// own keychain partition, and <c>SecureStorage</c> offers no way to
/// name a shared access group. Reading the app's entry from the
/// extension therefore has to go to the Security framework
/// directly.</para>
///
/// <para><b>The access group is the whole mechanism.</b> Both bundles
/// must declare the same <c>keychain-access-groups</c> entitlement, and
/// that group has to be registered against the team in the Apple
/// Developer account. Without it this compiles perfectly and returns
/// nothing at runtime, which is the failure worth knowing about in
/// advance: it looks like a missing key rather than a missing
/// entitlement.</para>
///
/// <para>Compiled into both the app and the extension from this one
/// file, linked rather than copied. Two implementations of "which
/// keychain entry" is exactly how the two halves end up looking in
/// different places.</para>
/// </summary>
public static class SharedKeychain
{
	/// <summary>
	/// The access group, which must match the entitlement in both
	/// bundles. The team prefix is supplied by iOS at runtime when the
	/// group is written this way, so it is deliberately not hardcoded.
	/// </summary>
	public const string AccessGroup = "group.com.bleedingdeacons.hand";

	private const string Service = "com.bleedingdeacons.hand";
	private const string Account = "payload_key";

	/// <summary>
	/// The payload key, or empty when there is none to read.
	///
	/// <para>Empty covers a handset that has not enrolled since keys
	/// existed, a keychain the system has invalidated, and an access
	/// group that is not actually shared. The caller can do nothing
	/// different about any of them: the alert arrives unreadable either
	/// way, and Hand already has one answer for that.</para>
	/// </summary>
	public static string Read()
	{
		using var query = new SecRecord(SecKind.GenericPassword)
		{
			Service = Service,
			Account = Account,
			AccessGroup = AccessGroup,
		};

		var data = SecKeyChain.QueryAsData(query, false, out var status);

		if (status != SecStatusCode.Success || data is null)
		{
			return string.Empty;
		}

		return NSString.FromData(data, NSStringEncoding.UTF8)?.ToString() ?? string.Empty;
	}

	/// <summary>
	/// Store the payload key, replacing any entry already there.
	///
	/// <para>Deleted first rather than updated: an add over an existing
	/// entry fails with a duplicate-item status, and the update path
	/// needs a separate query anyway. Delete-then-add is one branch
	/// instead of two and cannot leave a stale value behind.</para>
	///
	/// <para><c>AccessibleAfterFirstUnlock</c> because a push can arrive
	/// while the phone is locked, which is most of the point — an
	/// extension that cannot read the key on a locked handset can only
	/// decrypt alerts that arrive when somebody is already holding the
	/// phone.</para>
	/// </summary>
	public static bool Write(string key)
	{
		Delete();

		if (string.IsNullOrEmpty(key))
		{
			return true;
		}

		using var record = new SecRecord(SecKind.GenericPassword)
		{
			Service = Service,
			Account = Account,
			AccessGroup = AccessGroup,
			Accessible = SecAccessible.AfterFirstUnlock,
			ValueData = NSData.FromString(key, NSStringEncoding.UTF8),
		};

		return SecKeyChain.Add(record) == SecStatusCode.Success;
	}

	/// <summary>Forget the key. Signing out forgets both secrets.</summary>
	public static void Delete()
	{
		using var query = new SecRecord(SecKind.GenericPassword)
		{
			Service = Service,
			Account = Account,
			AccessGroup = AccessGroup,
		};

		SecKeyChain.Remove(query);
	}
}
