using System.Reflection;
using CommunityToolkit.Maui;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using TheBleedingDeacons.Intergroup.Hand.Services;
using TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Hand.Support;
using TheBleedingDeacons.Intergroup.Hand.ViewModels;
using TheBleedingDeacons.Intergroup.Hand.Views;

namespace TheBleedingDeacons.Intergroup.Hand;

public static class MauiProgram
{
	// Factory that produces a fresh base-logger configuration (file/console/debug
	// sinks + enrichers). Captured during SetupSerilog so BetterStackLoggerController
	// can rebuild the whole pipeline on demand when Better Stack settings change.
	// Null until SetupSerilog runs.
	private static Func<LoggerConfiguration>? _baseLoggerFactory;

	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();

		// ── Load appsettings.json from embedded resource ──────────────
		// MAUI does not auto-load appsettings.json the way ASP.NET Core does.
		// The file is embedded in the assembly (see csproj <EmbeddedResource>)
		// and must be loaded explicitly so Serilog's ReadFrom.Configuration
		// and any builder.Configuration[...] lookups actually return values.
		var assembly = Assembly.GetExecutingAssembly();
		using (var stream = assembly.GetManifestResourceStream(
			"TheBleedingDeacons.Intergroup.Hand.appsettings.json"))
		{
			if (stream is not null)
			{
				var jsonConfig = new ConfigurationBuilder()
					.AddJsonStream(stream)
					.Build();
				builder.Configuration.AddConfiguration(jsonConfig);
			}
			else
			{
				System.Diagnostics.Debug.WriteLine(
					"WARNING: appsettings.json embedded resource not found. " +
					"Available resources: " +
					string.Join(", ", assembly.GetManifestResourceNames()));
			}
		}

		// ── Layer devsettings.json on top, if present ─────────────────
		// Only embedded when built with UseDevCredentials=true (the default).
		// It overrides appsettings.json — most notably App:Environment, so log
		// entries are tagged correctly. Production builds skip this because the
		// resource does not exist in the assembly.
		using (var stream = assembly.GetManifestResourceStream(
			"TheBleedingDeacons.Intergroup.Hand.devsettings.json"))
		{
			if (stream is not null)
			{
				var devConfig = new ConfigurationBuilder()
					.AddJsonStream(stream)
					.Build();
				builder.Configuration.AddConfiguration(devConfig);
			}
		}

		builder
			.UseMauiApp<App>()
			.UseMauiCommunityToolkit()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		SetupSerilog(builder);

		// Bridge Serilog into Microsoft.Extensions.Logging so that ILogger<T>
		// resolved from DI flows through the Serilog pipeline.
		builder.Logging.AddSerilog();

		// Ensure Serilog is flushed on unhandled / fatal errors.
		RegisterGlobalExceptionHandlers();

		builder.Services.AddSingleton<IConfigurationService, ConfigurationService>();

		// --- HttpClient ---
		//
		// Platform-native handler, for the reason Register documents at length:
		// some shared-hosting edge WAFs fingerprint TLS (JA3/JA4) and block
		// .NET's managed SocketsHttpHandler while allowing the platform's native
		// stack — the same stack the system browser uses. Reach sits behind that
		// same WAF, so Hand's API traffic must look like ordinary traffic or it
		// gets turned away at the edge.
		//
		//   Windows       → WinHttpHandler         (schannel / WinHTTP)
		//   Android       → AndroidMessageHandler  (OkHttp)
		//   iOS / MacCat  → NSUrlSessionHandler    (NSURLSession)
		//   Other         → HttpClientHandler      (managed fallback)
		//
		// The keyed "betterstack" client is separate and managed: Better Stack
		// is not behind that WAF, and WinHttpHandler has a known race
		// (dotnet/runtime#22749) where a pooled keep-alive connection closed
		// server-side surfaces as WinHttpException 12152 on the next reuse —
		// which fires on CloseAndFlush at shutdown, after the sink has been idle.
		builder.Services.AddSingleton<HttpClient>(_ => CreateHttpClient());
		builder.Services.AddKeyedSingleton<HttpClient>("betterstack", (_, _) => CreateBetterStackHttpClient());

		// Better Stack logger controller — rebuilds the Serilog pipeline on
		// demand. Singleton so all callers share the serialisation lock inside.
		builder.Services.AddSingleton<IBetterStackLoggerController>(sp =>
		{
			if (_baseLoggerFactory is null)
			{
				throw new InvalidOperationException(
					"Serilog base-logger factory was not captured. SetupSerilog must run before the DI container is built.");
			}

			var httpClient = sp.GetRequiredKeyedService<HttpClient>("betterstack");
			return new BetterStackLoggerController(_baseLoggerFactory, httpClient);
		});

		// ── Hand services ─────────────────────────────────────────────
		builder.Services.AddSingleton<IReachClient, ReachClient>();
		builder.Services.AddSingleton<IPushRegistrar, PushRegistrar>();
		builder.Services.AddSingleton<IDeviceAuthService, DeviceAuthService>();

		// The alarm owns audio and vibration and must outlive any page: an
		// alert that arrives while the app is backgrounded starts it, and
		// navigating afterwards must not silence it.
		builder.Services.AddSingleton<IAlertAlarm, AlertAlarm>();

		// Singleton, and the only thing that talks to the alert endpoints:
		// it owns the poll timer and the de-duplication of alerts arriving by
		// both push and poll, neither of which survives being rebuilt per page.
		builder.Services.AddSingleton<IAlertService, AlertService>();

		builder.Services.AddSingleton<IPlatformAlertPresenter, PlatformAlertPresenter>();

		// ── Views and view-models ─────────────────────────────────────
		builder.Services.AddSingleton<AlertsPage>();
		builder.Services.AddSingleton<AlertsViewModel>();
		builder.Services.AddTransient<SignInPage>();
		builder.Services.AddTransient<SignInViewModel>();
		builder.Services.AddTransient<SettingsPage>();
		builder.Services.AddTransient<SettingsViewModel>();

#if DEBUG
		builder.Services.AddLogging();
		builder.Logging.AddDebug();
#endif

		var app = builder.Build();

		// ── Attach the Better Stack sink using saved settings ─────────
		// SetupSerilog runs before DI is built, so it cannot read from
		// ConfigurationService. Once the container exists we ask the controller
		// to layer the durable HTTP sink onto the base pipeline. The same
		// controller is used by the settings view-model, so runtime changes go
		// through one code path that tears the previous sink down cleanly.
		using (var scope = app.Services.CreateScope())
		{
			var configService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
			var controller = scope.ServiceProvider.GetRequiredService<IBetterStackLoggerController>();
			controller.Reconfigure(configService.GetBetterStackConfiguration());
		}

		return app;
	}

	private static void SetupSerilog(MauiAppBuilder builder)
	{
		var logPath = Path.Combine(FileSystem.AppDataDirectory, "logs");
		Directory.CreateDirectory(logPath);

		// Both feed Serilog enrichers, and appName also forms the log filename,
		// so neither may be null. appsettings.json is git-ignored and CI writes a
		// `{}` placeholder, so a build without a populated config is a real
		// possibility rather than a theoretical one.
		var appName = builder.Configuration["App:Name"] ?? "Hand";
		var environment = builder.Configuration["App:Environment"] ?? "Development";

		// Captured because builder.Configuration won't be in scope once DI is built.
		var configRef = builder.Configuration;
		_baseLoggerFactory = () => BuildBaseLoggerConfiguration(configRef, logPath, appName, environment);

		Log.Logger = _baseLoggerFactory().CreateLogger();

		// Framework is its own property rather than folded into the message so
		// Better Stack can filter on it directly — the quickest way to tell one
		// runtime from another across a fleet of handsets.
		Log.Information(
			"Application {AppName} v{Version} (build {Build}, built {Built}) starting on {Platform} under {Framework}",
			appName, BuildInfo.Version, BuildInfo.Build, BuildInfo.BuildTimestamp,
			DeviceInfo.Platform, BuildInfo.Framework);
	}

	/// <summary>
	/// Builds a fresh <see cref="LoggerConfiguration"/> containing only the sinks
	/// fixed for the lifetime of the process — file, Debug, and (on desktop)
	/// console — plus all standard enrichers. The durable Better Stack sink is
	/// layered on separately by <see cref="BetterStackLoggerController"/> because
	/// it can be reconfigured at runtime.
	///
	/// Returning a configuration rather than a built logger lets the controller
	/// chain <c>.WriteTo.DurableHttp…</c> before calling <c>CreateLogger()</c>,
	/// giving one unified pipeline rather than nested ones.
	/// </summary>
	private static LoggerConfiguration BuildBaseLoggerConfiguration(
		IConfiguration config,
		string logPath,
		string appName,
		string environment)
	{
		var cfg = new LoggerConfiguration()
			.ReadFrom.Configuration(config)
			.Enrich.WithProperty("Application", appName)
			.Enrich.WithProperty("Environment", environment)
			.Enrich.WithProperty("Platform", DeviceInfo.Platform.ToString())
			.Enrich.WithProperty("PlatformVersion", DeviceInfo.VersionString)
			.Enrich.WithProperty("AppVersion", AppVersion())
			.Enrich.WithProperty("DeviceModel", DeviceInfo.Model)
			.Enrich.WithProperty("DeviceName", DeviceInfo.Name)
			.Enrich.WithProperty("ProcessId", Environment.ProcessId)
			// Environment.MachineName returns "localhost" on Android and a
			// sandbox name on iOS, so a user-set label with a platform-aware
			// default is what actually distinguishes handsets in the live tail.
			.Enrich.WithProperty("DeviceLabel", ResolveDeviceLabel())
			.Enrich.With<ExceptionEnricher>();

#if DEBUG
		cfg = cfg
			.WriteTo.File(
				Path.Combine(logPath, $"{appName.ToLowerInvariant()}-debug-.log"),
				rollingInterval: RollingInterval.Day,
				retainedFileCountLimit: 21)
			.WriteTo.Debug();

		// The Serilog console sink calls Console.set_ForegroundColor, which throws
		// PlatformNotSupportedException on Android and iOS — every log event then
		// hits SelfLog with a stack trace and drowns real diagnostics. On mobile
		// the Debug sink already surfaces logs to the IDE, so scope this to desktop.
#if WINDOWS || MACCATALYST
		cfg = cfg.WriteTo.Console();
#endif
#else
		cfg = cfg.WriteTo.File(
			Path.Combine(logPath, $"{appName.ToLowerInvariant()}-.log"),
			rollingInterval: RollingInterval.Day,
			retainedFileCountLimit: 7,
			restrictedToMinimumLevel: LogEventLevel.Information);
#endif

		return cfg;
	}

	// Mirrors ConfigurationService.DeviceLabel but reads Preferences directly so
	// SetupSerilog can call it before the DI container exists.
	private const string DeviceLabelPreferenceKey = "device_label";

	private static string ResolveDeviceLabel()
	{
		try
		{
			var stored = Preferences.Get(DeviceLabelPreferenceKey, string.Empty);
			if (!string.IsNullOrWhiteSpace(stored))
			{
				return stored;
			}
		}
		catch
		{
			// Preferences unavailable — fall through to the auto-default.
		}

		try
		{
			var platform = DeviceInfo.Platform;

			if (platform == DevicePlatform.WinUI || platform == DevicePlatform.MacCatalyst)
			{
				var machine = Environment.MachineName;
				if (!string.IsNullOrWhiteSpace(machine)
					&& !string.Equals(machine, "localhost", StringComparison.OrdinalIgnoreCase))
				{
					return machine;
				}
			}

			var manufacturer = (DeviceInfo.Manufacturer ?? string.Empty).Trim();
			var model = (DeviceInfo.Model ?? string.Empty).Trim();
			var osName = platform.ToString();
			var osVersion = (DeviceInfo.VersionString ?? string.Empty).Trim();

			var hardware = manufacturer.Length > 0
				&& !model.StartsWith(manufacturer, StringComparison.OrdinalIgnoreCase)
				? $"{manufacturer} {model}".Trim()
				: model;

			if (string.IsNullOrWhiteSpace(hardware))
			{
				hardware = "Device";
			}

			return osVersion.Length == 0
				? $"{hardware} ({osName})"
				: $"{hardware} ({osName} {osVersion})";
		}
		catch
		{
			return "UnknownDevice";
		}
	}

	private static void RegisterGlobalExceptionHandlers()
	{
		// Logging from a crash path must itself be crash-proof. If Log.Fatal
		// throws — a disposed pipeline, an enricher faulting on this particular
		// exception — we must not replace the original crash with a logger crash.

		AppDomain.CurrentDomain.UnhandledException += (_, args) =>
		{
			try
			{
				if (args.ExceptionObject is Exception ex)
				{
					Log.Fatal(ex, "Unhandled AppDomain exception (IsTerminating={IsTerminating})", args.IsTerminating);
				}
				else
				{
					Log.Fatal("Unhandled AppDomain exception: {ExceptionObject}", args.ExceptionObject);
				}
			}
			catch
			{
				// Never throw from a crash handler.
			}

			TryFlushLogs();
		};

		// Unobserved Task exceptions — the app usually survives, so log but
		// don't close.
		TaskScheduler.UnobservedTaskException += (_, args) =>
		{
			try
			{
				Log.Error(args.Exception, "Unobserved task exception");
			}
			catch
			{
				// Never throw from a crash handler.
			}
		};

#if ANDROID
		// Java-side unhandled exceptions bridged into .NET.
		Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (_, args) =>
		{
			try
			{
				Log.Fatal(args.Exception, "Unhandled Android exception");
			}
			catch
			{
				// Never throw from a crash handler.
			}

			TryFlushLogs();
		};
#endif
	}

	/// <summary>
	/// Close and flush all Serilog sinks with a bounded wait, never throwing.
	/// <c>Log.CloseAndFlush()</c> is synchronous with no timeout; if the durable
	/// sink's final POST is slow or the endpoint unreachable it can block
	/// shutdown for up to <see cref="HttpClient.Timeout"/>. Anything still on
	/// disk after the cap ships on the next launch — that is the durable sink's
	/// entire purpose.
	/// </summary>
	internal static void TryFlushLogs(TimeSpan? timeout = null)
	{
		try
		{
			Task.Run(() => Log.CloseAndFlush()).Wait(timeout ?? TimeSpan.FromSeconds(5));
		}
		catch
		{
			// Never throw from a shutdown / crash path.
		}
	}

	/// <summary>
	/// An HttpClient backed by the platform's native HTTP handler. See the
	/// registration above for why that matters.
	/// </summary>
	private static HttpClient CreateHttpClient()
	{
		HttpMessageHandler handler;

#if WINDOWS
		handler = new System.Net.Http.WinHttpHandler
		{
			AutomaticDecompression = System.Net.DecompressionMethods.GZip
				| System.Net.DecompressionMethods.Deflate
				| System.Net.DecompressionMethods.Brotli,
			AutomaticRedirection = true,
		};
#elif ANDROID
		handler = new Xamarin.Android.Net.AndroidMessageHandler
		{
			AutomaticDecompression = System.Net.DecompressionMethods.GZip
				| System.Net.DecompressionMethods.Deflate
				| System.Net.DecompressionMethods.Brotli,
		};
#elif IOS || MACCATALYST
		// NSUrlSessionHandler honours the system's default decompression
		// transparently; no AutomaticDecompression property is exposed.
		handler = new NSUrlSessionHandler();
#else
		handler = new HttpClientHandler
		{
			AutomaticDecompression = System.Net.DecompressionMethods.GZip
				| System.Net.DecompressionMethods.Deflate
				| System.Net.DecompressionMethods.Brotli,
		};
#endif

		return new HttpClient(handler, disposeHandler: true)
		{
			// Tighter than Register's 100s: every call this client makes is on a
			// path where a responder is waiting, and a request still outstanding
			// after half a minute has already failed as far as a shift is
			// concerned. The poll retries on its next tick regardless.
			Timeout = TimeSpan.FromSeconds(30),
		};
	}

	/// <summary>
	/// The HttpClient used exclusively by the Better Stack log sink. Managed
	/// <see cref="SocketsHttpHandler"/> on every platform, with an aggressive
	/// pooled-connection idle timeout — see the registration above and Register's
	/// MauiProgram for the WinHttpException 12152 race this avoids.
	/// </summary>
	private static HttpClient CreateBetterStackHttpClient()
	{
		var handler = new SocketsHttpHandler
		{
			PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
			PooledConnectionLifetime = TimeSpan.FromMinutes(5),
			AutomaticDecompression = System.Net.DecompressionMethods.GZip
				| System.Net.DecompressionMethods.Deflate
				| System.Net.DecompressionMethods.Brotli,
		};

		return new HttpClient(handler, disposeHandler: true)
		{
			// Fail fast and let the durable sink retry from its on-disk buffer
			// rather than blocking shutdown behind a slow response.
			Timeout = TimeSpan.FromSeconds(30),
		};
	}

	public static string AppVersion()
	{
		if (DeviceInfo.Platform == DevicePlatform.WinUI)
		{
			return System.Diagnostics.FileVersionInfo
				.GetVersionInfo(Environment.ProcessPath!)
				.FileVersion ?? AppInfo.VersionString;
		}

		return AppInfo.VersionString;
	}
}
