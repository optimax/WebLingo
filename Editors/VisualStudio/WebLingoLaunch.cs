// WebLingo's launch targets for Visual Studio's Open Folder (Editors/README.md, "Visual Studio, with WebLingo's
// extension"): F5 on a .wlg script or a .wasp page, and launch.vs.json's configurations of the type "weblingo", each
// debugged by weblingo's or wasp's debug adapter, which Visual Studio's Debug Adapter Host runs as an engine of its own,
// registered by WebLingo.pkgdef. Compiled for .NET Framework 4.8 by build.ps1, with the compiler and the assemblies
// Visual Studio installs, which it references and never ships.

using System;
using System.Collections;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Debug;
using Microsoft.VisualStudio.Workspace.Extensions.VS.Debug;

namespace WebLingo.VisualStudio
{
    // What a launch is: its program, its folder, its arguments, and the adapter that debugs it.
    internal static class WebLingoLaunch
    {
        // The engines WebLingo.pkgdef registers with the Debug Adapter Host: weblingo's, for scripts, and wasp's, for pages
        // and sites.
        public static readonly Guid ScriptEngine = new Guid("C1E04249-C50E-4348-97B3-BC63D6362C41");
        public static readonly Guid PageEngine = new Guid("6AB59EE8-7802-4FEA-B430-5090D36C0927");

        // A launch's engine, its program, its folder, and its options, the adapter's launch request.
        public sealed class Target
        {
            public Guid Engine;
            public string Exe;
            public string Folder;
            public string Options;
        }

        // The launch's settings, a launch.vs.json's configuration, or those F5 on a file makes, made the adapter's launch
        // request (Editors/README.md, "Any debugger: the launch settings"), which the Debug Adapter Host gives it.
        public static Target Prepare(DebugLaunchActionContext context)
        {
            var settings = context.LaunchConfiguration;
            var program = Text(settings, "program") ?? Text(settings, LaunchConfigurationConstants.ProjectKey);
            var root = Text(settings, "root");
            var cwd = Text(settings, "cwd") ?? (program != null && Path.IsPathRooted(program) ? Path.GetDirectoryName(program) : null);
            var page = program == null || program.EndsWith(".wasp", StringComparison.OrdinalIgnoreCase);
            var json = new StringBuilder("{");
            var adapter = Find(page ? "wasp.exe" : "weblingo.exe");
            if (adapter != null)
            {
                Add(json, "$adapter", adapter);
                Add(json, "$adapterArgs", "--debug-adapter");
            }
            Add(json, "request", "launch");
            Add(json, "program", program);
            Add(json, "cwd", cwd);
            Add(json, "root", root);
            foreach (var flag in new[] { "stopOnEntry", "noDebug" })
            {
                var value = Text(settings, flag);
                if (value != null)
                {
                    json.Append(json.Length > 1 ? "," : "").Append(Quote(flag)).Append(':').Append(value == "True" || value == "true" ? "true" : "false");
                }
            }
            var port = Text(settings, "port");
            if (port != null && int.TryParse(port, out var number))
            {
                json.Append(json.Length > 1 ? "," : "").Append(Quote("port")).Append(':').Append(number);
            }
            var args = Value(settings, LaunchConfigurationConstants.ArgsKey);
            if (args is IEnumerable list && !(args is string))
            {
                json.Append(json.Length > 1 ? "," : "").Append(Quote("args")).Append(":[");
                var first = true;
                foreach (var arg in list)
                {
                    json.Append(first ? "" : ",").Append(Quote(Convert.ToString(arg)));
                    first = false;
                }
                json.Append(']');
            }
            json.Append('}');
            return new Target { Engine = page ? PageEngine : ScriptEngine, Exe = program ?? root ?? cwd ?? "WASP", Folder = cwd,
                                Options = json.ToString() };
        }

        // A setting's value, or null.
        private static object Value(IPropertySettings settings, string key)
        {
            if (settings == null)
            {
                return null;
            }
            return PropertySettingsHelper.TryGetValue(settings, key, out object value) ? value : null;
        }

        // A setting's value as text, or null where it has none, or an empty one.
        private static string Text(IPropertySettings settings, string key)
        {
            var text = Convert.ToString(Value(settings, key));
            return string.IsNullOrEmpty(text) ? null : text;
        }

        // An adapter's program: where the PATH finds it, as the installer puts WebLingo's folder on it, else WebLingo's own
        // folder in Program Files; or null, the engine's registered path then used.
        private static string Find(string name)
        {
            foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                try
                {
                    var path = Path.Combine(folder.Trim().Trim('"'), name);
                    if (folder.Trim().Length > 0 && File.Exists(path))
                    {
                        return path;
                    }
                }
                catch (ArgumentException)
                {
                    // A folder on the PATH that is no path.
                }
            }
            var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WebLingo", name);
            return File.Exists(installed) ? installed : null;
        }

        private static void Add(StringBuilder json, string name, string value)
        {
            if (value != null)
            {
                json.Append(json.Length > 1 ? "," : "").Append(Quote(name)).Append(':').Append(Quote(value));
            }
        }

        // A string as JSON writes it.
        private static string Quote(string text)
        {
            var quoted = new StringBuilder("\"");
            foreach (var c in text ?? "")
            {
                switch (c)
                {
                    case '"': quoted.Append("\\\""); break;
                    case '\\': quoted.Append("\\\\"); break;
                    case '\n': quoted.Append("\\n"); break;
                    case '\r': quoted.Append("\\r"); break;
                    case '\t': quoted.Append("\\t"); break;
                    default:
                        if (c < ' ') quoted.Append("\\u").Append(((int)c).ToString("x4"));
                        else quoted.Append(c);
                        break;
                }
            }
            return quoted.Append('"').ToString();
        }
    }

    // F5 on a .wlg script or a .wasp page, the file the startup item.
    [ExportVsDebugLaunchTarget(ProviderType, new[] { ".wlg", ".wasp" }, ProviderPriority.Normal)]
    internal sealed class WebLingoFileLaunchTarget : IVsDebugLaunchTargetProvider
    {
        public const string ProviderType = "F9412734-CB2D-40D4-A984-07AF1DC49E60";

        public void SetupDebugTargetInfo(ref VsDebugTargetInfo vsDebugTargetInfo, DebugLaunchActionContext debugLaunchContext)
        {
            var target = WebLingoLaunch.Prepare(debugLaunchContext);
            vsDebugTargetInfo.dlo = DEBUG_LAUNCH_OPERATION.DLO_CreateProcess;
            vsDebugTargetInfo.clsidCustom = target.Engine;
            vsDebugTargetInfo.bstrExe = target.Exe;
            vsDebugTargetInfo.bstrCurDir = target.Folder;
            vsDebugTargetInfo.bstrOptions = target.Options;
        }
    }

    // A launch.vs.json configuration of the type "weblingo": a script, a page, or, without a program, a site.
    [ExportVsDebugLaunchTarget2(ProviderType, "weblingo", ProviderPriority.Normal)]
    internal sealed class WebLingoConfigurationLaunchTarget : IVsDebugLaunchTargetProvider2
    {
        public const string ProviderType = "C603F526-890B-4ABD-A3DD-5F5EA8EA28CE";

        public void UpdateContext(DebugLaunchActionContext debugLaunchContext)
        {
        }

        public void SetupDebugTargetInfo(ref VsDebugTargetInfo4 vsDebugTargetInfo, DebugLaunchActionContext debugLaunchContext)
        {
            var target = WebLingoLaunch.Prepare(debugLaunchContext);
            vsDebugTargetInfo.dlo = (uint)DEBUG_LAUNCH_OPERATION.DLO_CreateProcess;
            vsDebugTargetInfo.guidLaunchDebugEngine = target.Engine;
            vsDebugTargetInfo.bstrExe = target.Exe;
            vsDebugTargetInfo.bstrCurDir = target.Folder;
            vsDebugTargetInfo.bstrOptions = target.Options;
        }
    }
}
