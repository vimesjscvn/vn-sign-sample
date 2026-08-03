using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace VMSign.AppE2E;

internal sealed class VMSignUiSession : IDisposable
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    private readonly Application _application;
    private readonly int _processId;
    private readonly string _configDirectory;
    private bool _disposed;

    private VMSignUiSession(
        Application application,
        UIA3Automation automation,
        Window mainWindow,
        string configDirectory)
    {
        _application = application;
        _processId = application.ProcessId;
        _configDirectory = configDirectory;
        Automation = automation;
        MainWindow = mainWindow;
    }

    public UIA3Automation Automation { get; }

    public Window MainWindow { get; }

    public int ProcessId => _processId;

    public static VMSignUiSession Start()
    {
        var executable = VMSignExecutable.Find();
        var configDirectory = Path.Combine(
            Path.GetTempPath(),
            $"vmsign-e2e-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(configDirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("--disable-updates");
        startInfo.Environment["VMSIGN_CONFIG_DIR"] = configDirectory;
        // The live test injects credentials through UI Automation. Do not also
        // expose them through the child process environment.
        startInfo.Environment.Remove("VMSIGN_MYSIGN_USERNAME");
        startInfo.Environment.Remove("VMSIGN_MYSIGN_PASSWORD");

        Application application;
        try
        {
            application = Application.Launch(startInfo);
        }
        catch
        {
            DeleteConfigDirectory(configDirectory);
            throw;
        }

        var automation = new UIA3Automation();

        try
        {
            var mainWindow = WaitUntilNotNull(
                () => application.GetMainWindow(automation),
                DefaultTimeout,
                "the VMSign main window");

            mainWindow.Focus();
            return new VMSignUiSession(application, automation, mainWindow, configDirectory);
        }
        catch
        {
            automation.Dispose();
            StopProcess(application.ProcessId);
            DeleteConfigDirectory(configDirectory);
            throw;
        }
    }

    public AutomationElement RequireByAutomationId(string automationId, TimeSpan? timeout = null) =>
        WaitUntilNotNull(
            () => FindByAutomationId(automationId),
            timeout ?? DefaultTimeout,
            $"AutomationId '{automationId}'");

    public AutomationElement RequireByName(string name, TimeSpan? timeout = null) =>
        WaitUntilNotNull(
            () => FindByName(name),
            timeout ?? DefaultTimeout,
            $"element named '{name}'");

    public AutomationElement? FindByAutomationId(string automationId) =>
        FindInProcess(factory => factory.ByAutomationId(automationId));

    public AutomationElement? FindByName(string name) =>
        FindInProcess(factory => factory.ByName(name));

    public void WaitUntil(Func<bool> predicate, string description, TimeSpan? timeout = null)
    {
        var stopwatch = Stopwatch.StartNew();
        Exception? lastError = null;
        while (stopwatch.Elapsed < (timeout ?? DefaultTimeout))
        {
            try
            {
                if (predicate())
                {
                    return;
                }
            }
            catch (Exception error)
            {
                lastError = error;
            }

            Thread.Sleep(100);
        }

        throw new TimeoutException($"Timed out waiting for {description}.", lastError);
    }

    public string DumpUiTree()
    {
        var lines = MainWindow.FindAllDescendants()
            .Select(element =>
            {
                string automationId;
                string name;
                string controlType;
                try { automationId = element.AutomationId ?? string.Empty; } catch { automationId = string.Empty; }
                try { name = element.Name ?? string.Empty; } catch { name = string.Empty; }
                try { controlType = element.ControlType.ToString(); } catch { controlType = "Unknown"; }
                return $"{controlType,-16} id='{automationId}' name='{name}'";
            });

        return string.Join(Environment.NewLine, lines);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            var process = Process.GetProcessById(_processId);
            if (!process.HasExited)
            {
                process.CloseMainWindow();
                if (!process.WaitForExit(3_000))
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(3_000);
                }
            }
        }
        catch (ArgumentException)
        {
            // The application already exited.
        }
        finally
        {
            Automation.Dispose();
            DeleteConfigDirectory(_configDirectory);
        }
    }

    private AutomationElement? FindInProcess(Func<FlaUI.Core.Conditions.ConditionFactory, FlaUI.Core.Conditions.ConditionBase> condition)
    {
        var processCondition = Automation.ConditionFactory.ByProcessId(_processId);
        return Automation.GetDesktop().FindFirstDescendant(factory => processCondition.And(condition(factory)));
    }

    private static T WaitUntilNotNull<T>(Func<T?> action, TimeSpan timeout, string description)
        where T : class
    {
        var stopwatch = Stopwatch.StartNew();
        Exception? lastError = null;
        while (stopwatch.Elapsed < timeout)
        {
            try
            {
                var result = action();
                if (result is not null)
                {
                    return result;
                }
            }
            catch (Exception error)
            {
                lastError = error;
            }

            Thread.Sleep(100);
        }

        throw new TimeoutException($"Timed out waiting for {description}.", lastError);
    }

    private static void StopProcess(int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(3_000);
            }
        }
        catch (ArgumentException)
        {
            // The process exited while startup was being inspected.
        }
    }

    private static void DeleteConfigDirectory(string configDirectory)
    {
        try
        {
            if (Directory.Exists(configDirectory))
            {
                Directory.Delete(configDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup. The exact per-test directory can only be locked
            // briefly while the just-stopped process flushes its log file.
        }
        catch (UnauthorizedAccessException)
        {
            // Do not hide the original test outcome because of temp cleanup.
        }
    }
}
