using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class DesktopCategoryClassifierTests
{
    [TestMethod]
    public void ClassifyShellNamespace_ReturnsStableSystemCategory()
    {
        DesktopCategoryDefinition category = DesktopCategoryClassifier.ClassifyShellNamespace();

        Assert.AreEqual("system-items", category.Key);
        Assert.AreEqual("系统项目", category.DisplayName);
    }

    [TestMethod]
    [DataRow("shortcut.lnk", "shortcuts")]
    [DataRow("document.DOCX", "documents")]
    [DataRow("budget.xlsx", "spreadsheets")]
    [DataRow("slides.pptx", "presentations")]
    [DataRow("photo.png", "images")]
    [DataRow("movie.mkv", "videos")]
    [DataRow("music.flac", "audio")]
    [DataRow("archive.7z", "archives")]
    [DataRow("setup.msix", "applications")]
    [DataRow("source.cs", "code")]
    [DataRow("records.sqlite", "data")]
    [DataRow("model.blend", "design")]
    [DataRow("book.epub", "ebooks")]
    [DataRow("font.woff2", "fonts")]
    [DataRow("disk.vhdx", "disk-images")]
    [DataRow("unknown.custom-extension", "other")]
    public void Classify_FileExtension_ReturnsExpectedCategory(string fileName, string expectedKey)
    {
        string path = Path.Combine(Path.GetTempPath(), fileName);

        DesktopCategoryDefinition category = DesktopCategoryClassifier.Classify(path);

        Assert.AreEqual(expectedKey, category.Key);
    }

    [TestMethod]
    public void Classify_DirectoryWithProjectMarker_ReturnsDevelopmentProjects()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "package.json"), "{}");

        DesktopCategoryDefinition category = DesktopCategoryClassifier.Classify(directory.Path);

        Assert.AreEqual("development-projects", category.Key);
    }

    [TestMethod]
    public void Classify_DirectoryWithGitFolder_ReturnsDevelopmentProjects()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(directory.Path, ".git"));

        DesktopCategoryDefinition category = DesktopCategoryClassifier.Classify(directory.Path);

        Assert.AreEqual("development-projects", category.Key);
    }

    [TestMethod]
    public void Classify_OrdinaryDirectory_ReturnsFolders()
    {
        using var directory = new TemporaryDirectory();

        DesktopCategoryDefinition category = DesktopCategoryClassifier.Classify(directory.Path);

        Assert.AreEqual("folders", category.Key);
    }

    [TestMethod]
    public void ClassifyWithReliability_OrdinaryDirectory_IsReliable()
    {
        using var directory = new TemporaryDirectory();

        DesktopCategoryClassification classification =
            DesktopCategoryClassifier.ClassifyWithReliability(directory.Path);

        Assert.AreEqual("folders", classification.Category.Key);
        Assert.IsTrue(classification.IsReliable);
    }

    [TestMethod]
    public void ClassifyWithReliability_MissingPath_IsUnreliable()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");

        DesktopCategoryClassification classification =
            DesktopCategoryClassifier.ClassifyWithReliability(path);

        Assert.AreEqual("documents", classification.Category.Key);
        Assert.IsFalse(classification.IsReliable);
    }

    [TestMethod]
    public void ClassifyDirectory_WhenMarkerEnumerationFails_IsUnreliable()
    {
        using var directory = new TemporaryDirectory();

        DesktopCategoryClassification classification =
            DesktopCategoryClassifier.ClassifyDirectory(
                directory.Path,
                _ => throw new IOException("simulated marker scan failure"));

        Assert.AreEqual("folders", classification.Category.Key);
        Assert.IsFalse(classification.IsReliable);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "DesktopOrganizer.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // 测试清理失败不应掩盖分类断言结果。
            }
            catch (UnauthorizedAccessException)
            {
                // 杀毒软件短暂占用测试目录时交由系统临时目录后续清理。
            }
        }
    }
}
