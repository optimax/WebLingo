// WebLingo's launch targets for Visual Studio's Open Folder (Editors/README.md, "Visual Studio, with WebLingo's
// extension"): F5 on a .wlg script or a .wasp page, and launch.vs.json's configurations of the type "weblingo", each
// debugged by weblingo's or wasp's debug adapter, which Visual Studio's Debug Adapter Host runs as an engine of its own,
// registered by WebLingo.pkgdef. Compiled for .NET Framework 4.8 by build.ps1, with the compiler and the assemblies
// Visual Studio installs, which it references and never ships.

using System;
using System.Collections;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Debug;
using Microsoft.VisualStudio.Workspace.Extensions.VS.Debug;
using Microsoft.VisualStudio.Workspace.VSIntegration.Contracts;

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
        // request (Editors/README.md, "Any debugger: the launch settings"), which the Debug Adapter Host gives it; a path
        // in them from the folder Visual Studio opened, workspace, as Visual Studio gives a file's, F5's own among them.
        public static Target Prepare(DebugLaunchActionContext context, string workspace)
        {
            var settings = context.LaunchConfiguration;
            var file = context.ProjectFileContext?.FilePath;
            var project = Text(settings, LaunchConfigurationConstants.ProjectKey);
            var folder = workspace ?? FolderOf(file, project);
            file = Absolute(file, folder);
            var program = Absolute(Text(settings, "program") ?? file ?? project, folder);
            var root = Absolute(Text(settings, "root"), folder);
            var cwd = Absolute(Text(settings, "cwd"), folder)
                      ?? (program != null && Path.IsPathRooted(program) ? Path.GetDirectoryName(program) : folder);
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

        // The folder Visual Studio opened, its folder workspace's location, read by reflection, so that this assembly names
        // no type of Visual Studio's threading library, which a workspace's members name and whose version changes with
        // Visual Studio's own; null where there is none.
        public static string FolderOpened(object workspaces)
        {
            try
            {
                var workspace = workspaces == null ? null
                    : typeof(Microsoft.VisualStudio.Workspace.VSIntegration.IVsWorkspaceFactory).GetProperty("CurrentWorkspace")?.GetValue(workspaces);
                return workspace == null ? null : typeof(IWorkspace).GetProperty("Location")?.GetValue(workspace) as string;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // The folder Visual Studio opened: the file's full path less the project's path from the folder, where it ends with
        // it; else the file's own folder; null without a file.
        private static string FolderOf(string file, string project)
        {
            if (string.IsNullOrEmpty(file) || !Path.IsPathRooted(file))
            {
                return null;
            }
            var relative = project?.Replace('/', '\\').TrimStart('\\');
            if (!string.IsNullOrEmpty(relative) && !Path.IsPathRooted(relative)
                && file.EndsWith("\\" + relative, StringComparison.OrdinalIgnoreCase))
            {
                return file.Substring(0, file.Length - relative.Length - 1);
            }
            return Path.GetDirectoryName(file);
        }

        // A path made full from the folder, where it is relative and there is one; as it is otherwise.
        private static string Absolute(string path, string folder) =>
            path == null || folder == null || Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(folder, path));

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

        // The folder Visual Studio opened, which a file's path is given from.
        [Import(typeof(IVsFolderWorkspaceService), AllowDefault = true)]
        private object Workspaces { get; set; }

        public void SetupDebugTargetInfo(ref VsDebugTargetInfo vsDebugTargetInfo, DebugLaunchActionContext debugLaunchContext)
        {
            var target = WebLingoLaunch.Prepare(debugLaunchContext, WebLingoLaunch.FolderOpened(Workspaces));
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

        // The folder Visual Studio opened, which a configuration's relative paths are from.
        [Import(typeof(IVsFolderWorkspaceService), AllowDefault = true)]
        private object Workspaces { get; set; }

        public void UpdateContext(DebugLaunchActionContext debugLaunchContext)
        {
        }

        public void SetupDebugTargetInfo(ref VsDebugTargetInfo4 vsDebugTargetInfo, DebugLaunchActionContext debugLaunchContext)
        {
            var target = WebLingoLaunch.Prepare(debugLaunchContext, WebLingoLaunch.FolderOpened(Workspaces));
            vsDebugTargetInfo.dlo = (uint)DEBUG_LAUNCH_OPERATION.DLO_CreateProcess;
            vsDebugTargetInfo.guidLaunchDebugEngine = target.Engine;
            vsDebugTargetInfo.bstrExe = target.Exe;
            vsDebugTargetInfo.bstrCurDir = target.Folder;
            vsDebugTargetInfo.bstrOptions = target.Options;
        }
    }
}
