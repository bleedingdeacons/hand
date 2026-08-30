using TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Hand.Services;

/// <summary>
/// Keeps the alert history in one file in the app's private data
/// directory.
///
/// <para><b>A file rather than Preferences.</b> Preferences is backed by
/// SharedPreferences on Android and the registry on Windows, both of
/// which are meant for small settings; a few hundred alerts is neither
/// small nor a setting. It is also not <c>SecureStorage</c>: the history
/// holds the same text that already reached the lock screen, and putting
/// it behind the keystore would cost a decryption on every read while
/// protecting nothing that is not already public to whoever holds the
/// unlocked phone.</para>
///
/// <para><b>Written through a temporary file.</b> A duty handset is
/// killed rather than closed, and a process that dies midway through
/// overwriting the file in place leaves a truncated document that will
/// not parse — losing the whole history to save part of one entry.
/// Writing beside it and moving into place makes the swap atomic, so the
/// worst a kill can do is lose the newest change.</para>
///
/// <para>The directory is <c>FileSystem.AppDataDirectory</c>, which is
/// private to the app and removed when it is uninstalled. That is the
/// right lifetime: a handset taken off the rota should not leave a
/// readable record of a night's callbacks behind it.</para>
/// </summary>
public sealed class AlertHistoryStore : IAlertHistoryStore
{
	private const string FileName = "alert-history.json";

	private readonly string _path = Path.Combine(FileSystem.AppDataDirectory, FileName);

	public async Task<string> ReadAsync()
	{
		if (!File.Exists(_path))
		{
			return string.Empty;
		}

		return await File.ReadAllTextAsync(_path).ConfigureAwait(false);
	}

	public async Task WriteAsync(string contents)
	{
		var temporary = _path + ".tmp";

		await File.WriteAllTextAsync(temporary, contents).ConfigureAwait(false);

		// Move rather than Replace: Replace requires the destination to
		// exist, and the first write of a handset's life has no file to
		// replace.
		File.Move(temporary, _path, overwrite: true);
	}
}
