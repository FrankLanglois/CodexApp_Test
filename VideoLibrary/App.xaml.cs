using System.Configuration;
using System.Data;
using System;
using System.IO;
using System.Windows;
using System.Threading.Tasks;

namespace VideoLibrary;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);

		AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
		DispatcherUnhandledException += App_DispatcherUnhandledException;
		TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
	}

	private static void LogException(string where, Exception ex)
	{
		try
		{
			var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VideoLibrary", "logs");
			Directory.CreateDirectory(dir);
			var file = Path.Combine(dir, $"error_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
			File.WriteAllText(file, $"Location: {where}\r\n{ex}");
		}
		catch { }
	}

	private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
	{
		LogException("DispatcherUnhandledException", e.Exception);
		// let it crash after logging
	}

	private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
	{
		if (e.ExceptionObject is Exception ex) LogException("CurrentDomain_UnhandledException", ex);
	}

	private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
	{
		LogException("TaskScheduler_UnobservedTaskException", e.Exception);
	}
}

