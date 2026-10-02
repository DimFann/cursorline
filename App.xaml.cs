using System.Threading;
using System.Windows;

namespace CursorLine;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
	private Mutex? _instanceMutex;

	protected override void OnStartup(StartupEventArgs e)
	{
		var mutex = new Mutex(true, @"Local\CursorLine.SingleInstance", out var createdNew);
		if (!createdNew)
		{
			mutex.Dispose();
			Shutdown();
			return;
		}

		_instanceMutex = mutex;
		base.OnStartup(e);

		MainWindow = new MainWindow();
		MainWindow.Show();
	}

	protected override void OnExit(ExitEventArgs e)
	{
		if (_instanceMutex is not null)
		{
			_instanceMutex.ReleaseMutex();
			_instanceMutex.Dispose();
		}

		base.OnExit(e);
	}
}

