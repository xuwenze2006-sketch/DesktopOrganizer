using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class CiOutputSafetyTests
{
    [TestMethod]
    public void SkipPublish_PreservesArtAndExistingPublishButClearsOldTestResults()
    {
        using var fixture = new Fixture();
        string art = fixture.Create("artifacts/scene-concept/source.kra", "original art");
        string backup = fixture.Create("artifacts/previous-release.exe", "backup");
        string publish = fixture.Create("artifacts/publish/win-x64/old.exe", "previous build");
        string stale = fixture.Create("artifacts/test-results/old.trx", "old tests");

        Assert.AreEqual(0, fixture.Run("-SkipPublish"), fixture.Output);
        Assert.AreEqual("original art", File.ReadAllText(art));
        Assert.AreEqual("backup", File.ReadAllText(backup));
        Assert.AreEqual("previous build", File.ReadAllText(publish));
        Assert.IsFalse(File.Exists(stale), fixture.Output);
    }

    [TestMethod]
    public void RuntimePathTraversal_IsRejectedBeforeAnyOutputIsDeleted()
    {
        using var fixture = new Fixture();
        string sentinel = fixture.Create("artifacts/test-results/old.trx", "keep");
        Assert.AreNotEqual(0, fixture.Run("-RuntimeIdentifier '../scene-concept' -SkipPublish"));
        Assert.AreEqual("keep", File.ReadAllText(sentinel));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "DesktopOrganizer.Tests", Guid.NewGuid().ToString("N"));
        public string Output { get; private set; } = "";

        public Fixture([CallerFilePath] string source = "")
        {
            string project = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", ".."));
            Create("scripts/ci.ps1", File.ReadAllText(Path.Combine(project, "scripts", "ci.ps1")));
            Create("DesktopOrganizer.slnx", "");
            Create("DesktopOrganizer.csproj", "");
            Create("tests/DesktopOrganizer.Tests/DesktopOrganizer.Tests.csproj", "");
        }

        public string Create(string relativePath, string content)
        {
            string path = Path.Combine(_root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        public int Run(string arguments)
        {
            string script = Path.Combine(_root, "scripts", "ci.ps1").Replace("'", "''");
            var start = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-NonInteractive");
            start.ArgumentList.Add("-ExecutionPolicy");
            start.ArgumentList.Add("Bypass");
            start.ArgumentList.Add("-Command");
            // 只执行真实脚本的路径/清理逻辑，不在夹具中启动实际构建。
            start.ArgumentList.Add("$ErrorActionPreference='Stop'; function global:dotnet { $global:LASTEXITCODE=0; if ($args[0] -eq '--version') { '10.0.302' } }; & '" + script + "' " + arguments);
            using var process = Process.Start(start)!;
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30000))
            {
                process.Kill(entireProcessTree: true);
                Assert.Fail("CI path test timed out.");
            }
            Output = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
            return process.ExitCode;
        }

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
