using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class TestProjectFilesTests
{
    [TestMethod]
    public void RegisteredSourceFiles_ExistInRepository()
    {
        Assert.IsNotEmpty(TestProjectFiles.RegisteredFiles);

        foreach (string fileName in TestProjectFiles.RegisteredFiles)
        {
            string path = TestProjectFiles.SourcePath(fileName);
            Assert.IsTrue(
                File.Exists(path),
                $"契约测试登记的源码文件不存在：{path}。源码目录调整后必须同步 TestProjectFiles 定位表。");
        }
    }
}
