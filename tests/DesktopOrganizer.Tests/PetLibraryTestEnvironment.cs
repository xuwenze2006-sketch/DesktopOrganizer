using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class PetLibraryTestEnvironment
{
    [AssemblyInitialize]
    public static void Initialize(TestContext _)
    {
        // 所有桌宠界面和动画测试共用独立空库，不能读取或改写用户实际的导入和隐藏偏好。
        string root = Path.Combine(TestProjectFiles.Root, "artifacts", "test-pet-library", Guid.NewGuid().ToString("N"));
        DesktopPetLibrary.InitializeShared(new DesktopPetLibrary(root));
    }
}
