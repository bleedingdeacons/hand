namespace TheBleedingDeacons.Intergroup.Hand.NotificationService;

/// <summary>
/// The extension's managed entry point, which nothing ever calls.
///
/// <para><b>Why an executable with an entry point nobody invokes.</b> An
/// iOS app extension is a separate binary that the system launches in
/// its own process, so the project is <c>OutputType=Exe</c> — and Roslyn
/// requires an executable to have a <c>Main</c>. iOS does not use it:
/// the extension host reads <c>NSExtensionPrincipalClass</c> from
/// <c>Info.plist</c> and instantiates
/// <see cref="AlertNotificationService"/> directly. So this method exists
/// to satisfy the compiler and is deliberately empty; putting startup
/// work here would be putting it somewhere that never runs.</para>
///
/// <para>The same shape as the app head's own
/// <c>Platforms/iOS/Program.cs</c>, which supplies its <c>Main</c> by
/// hand for the same reason — the difference being that the app's calls
/// <c>UIApplication.Main</c> and this one has nothing to call.</para>
///
/// <para><b>Not something the SDK generates for you.</b> .NET for iOS
/// does have a <c>Xamarin.GenerateMainStep</c>, but it is a linker step
/// that runs during the native build and produces the native
/// <c>main</c>; the managed entry point is the project's own. Without
/// this file the project fails to compile with <c>CS5001</c> — which
/// went unnoticed because nothing compiles it: CI builds the Android
/// head alone, and <c>ci.yml</c> records that the extension is unbuilt
/// source until somebody opens it on a Mac.</para>
/// </summary>
public static class Program
{
	/// <summary>
	/// Never invoked. See the type documentation.
	/// </summary>
	/// <param name="args">Ignored; iOS does not launch an extension this way.</param>
	private static void Main(string[] args)
	{
	}
}
