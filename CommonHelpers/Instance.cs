using System.Security.Principal;
using System.Security.AccessControl;
using Microsoft.Win32;
using System.Diagnostics;
using System.Reflection;
using System.IO;

namespace CommonHelpers
{
    public static class Instance
    {

        private static Mutex? runOnceMutex;
        private static Mutex? globalLockMutex;
        private static bool useKernelDrivers;

        private const String GLOBAL_MUTEX_NAME = "Global\\SteamDeckToolsCommonHelpers";
        private const int GLOBAL_DEFAULT_TIMEOUT = 10000;

        public static bool WantsRunOnStartup
        {
            get { return Environment.GetCommandLineArgs().Contains("-run-on-startup"); }
        }

        public static bool Uninstall
        {
            get { return Environment.GetCommandLineArgs().Contains("-uninstall"); }
        }

        public static bool IsDEBUG
        {
            get
            {
#if DEBUG
                return true;
#else
                return false;
#endif
            }
        }

        public static bool IsProductionBuild
        {
            get
            {
#if PRODUCTION_BUILD
                return true;
#else
                return false;
#endif
            }
        }

        public static void OnUninstall(Action action)
        {
            if (Uninstall)
            {
                action();
                Environment.Exit(0);
            }
        }

        public static Mutex? WaitGlobalMutex(int timeoutMs)
        {
            if (globalLockMutex == null)
                globalLockMutex = TryCreateOrOpenExistingMutex(GLOBAL_MUTEX_NAME);

            try
            {
                if (globalLockMutex.WaitOne(timeoutMs))
                    return globalLockMutex;
                return null;
            }
            catch (AbandonedMutexException)
            {
                return globalLockMutex;
            }
        }

        public static T? WithGlobalMutex<T>(int timeoutMs, Func<T?> func)
        {
            var mutex = WaitGlobalMutex(timeoutMs);
            if (mutex is null)
                return default(T);

            try
            {
                return func();
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }

        public static void RunOnce(String? title, String mutexName, int runOnceTimeout = 1000)
        {
            runOnceMutex = TryCreateOrOpenExistingMutex(mutexName);

            try
            {
                if (!runOnceMutex.WaitOne(runOnceTimeout))
                {
                    Fatal(title, "Run many times", false);
                }
            }
            catch (AbandonedMutexException)
            {
                // it is still OK
            }
        }

        public static String ApplicationName
        {
            get { return Assembly.GetEntryAssembly()?.GetName().Name ?? "unknown"; }
        }

        public static String ProductVersion
        {
            get => Application.ProductVersion;
        }

        public static String ProductVersionWithSha
        {
            get
            {
                var releaseVersion = typeof(Instance).Assembly.GetCustomAttributes<AssemblyInformationalVersionAttribute>().FirstOrDefault();
                return releaseVersion?.InformationalVersion ?? ProductVersion;
            }
        }

        public static bool HasFile(String name)
        {
            var currentProcess = Process.GetCurrentProcess();
            var currentDir = Path.GetDirectoryName(currentProcess.MainModule?.FileName);
            if (currentDir is null)
                return false;

            var uninstallExe = Path.Combine(currentDir, name);
            return File.Exists(uninstallExe);
        }

        private static System.Timers.Timer? updateTimer;

        public static void Fatal(String? title, String message, bool capture = true)
        {
            if (capture)
                Log.TraceError("FATAL: {0}", message);
            if (title is not null)
                MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            Environment.Exit(1);
        }

        private static Mutex TryCreateOrOpenExistingMutex(string name)
        {
            MutexSecurity mutexSecurity = new();
            SecurityIdentifier identity = new(WellKnownSidType.WorldSid, null);
            mutexSecurity.AddAccessRule(new MutexAccessRule(identity, MutexRights.Synchronize | MutexRights.Modify, AccessControlType.Allow));

            var mutex = new Mutex(false, name, out _);
            mutex.SetAccessControl(mutexSecurity);
            return mutex;
        }
    }
}
