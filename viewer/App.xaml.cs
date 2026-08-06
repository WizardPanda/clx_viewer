using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Windows;

namespace ClxViewer;

public partial class App : Application
{
    private const string MutexName = "ClxViewer.SingleInstance";
    private const string PipeName = "ClxViewer.OpenFiles";

    private Mutex? _mutex;
    private MainWindow? _main;
    private Thread? _pipeThread;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            ForwardToPrimary(e.Args);
            Shutdown();
            return;
        }

        StartPipeServer();
        _main = new MainWindow();
        MainWindow = _main;
        _main.Show();

        var files = e.Args.Where(a => a.EndsWith(".clx", StringComparison.OrdinalIgnoreCase)).ToList();
        if (files.Count > 0)
        {
            _main.OpenFiles(files);
        }

        // Headless export mode used for build-time verification.
        string? selftestDir = null;
        for (int i = 0; i < e.Args.Length - 1; i++)
        {
            if (e.Args[i] == "--selftest-export") selftestDir = e.Args[i + 1];
        }
        if (selftestDir != null && files.Count > 0)
        {
            Directory.CreateDirectory(selftestDir);
            _main.SelftestExport(selftestDir);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _pipeThread?.Join(TimeSpan.FromMilliseconds(500));
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private void StartPipeServer()
    {
        _pipeThread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName,
                        PipeDirection.In, 1, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);
                    server.WaitForConnection();
                    var buffer = new byte[4096];
                    int n = server.Read(buffer, 0, buffer.Length);
                    if (n > 0)
                    {
                        string text = Encoding.UTF8.GetString(buffer, 0, n);
                        var paths = text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                            .Where(p => p.EndsWith(".clx", StringComparison.OrdinalIgnoreCase))
                            .ToList();
                        if (paths.Count > 0)
                        {
                            Dispatcher.Invoke(() => _main?.OpenFiles(paths));
                            Dispatcher.Invoke(() =>
                            {
                                if (_main != null)
                                {
                                    _main.WindowState = WindowState.Normal;
                                    _main.Activate();
                                }
                            });
                        }
                    }
                }
                catch
                {
                    Thread.Sleep(100);
                }
            }
        })
        { IsBackground = true };
        _pipeThread.Start();
    }

    private static void ForwardToPrimary(string[] args)
    {
        var paths = args.Where(a => a.EndsWith(".clx", StringComparison.OrdinalIgnoreCase)).ToList();
        if (paths.Count == 0) return;
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(3000);
            byte[] data = Encoding.UTF8.GetBytes(string.Join("\n", paths));
            client.Write(data, 0, data.Length);
        }
        catch
        {
            // fall through: a second window is acceptable
        }
    }
}
