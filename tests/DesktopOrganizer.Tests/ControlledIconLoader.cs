using System.Collections.Concurrent;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopOrganizer.Tests;

/// <summary>仅替换原生图标提取；真实 STA 队列、去重、版本核验与 UI 回填保持运行。</summary>
internal sealed class ControlledIconLoader : IDisposable
{
    private readonly ManualResetEventSlim _release = new(true);
    private readonly ConcurrentQueue<(string Path, string Key)> _requests = new();
    public ShellIconLoadService Service { get; }
    public int LoadCount => _requests.Count;
    public string[] LoadedKeys => _requests.Select(request => request.Key).ToArray();

    public ControlledIconLoader() => Service = new ShellIconLoadService(Load);
    public void Hold() => _release.Reset();
    public void Release() => _release.Set();

    private BitmapSource Load(string path, string key)
    {
        _requests.Enqueue((path, key));
        if (!_release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("受控图标提取未释放。");
        var source = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null,
            new byte[] { 20, 100, 200, 255 }, 4);
        source.Freeze();
        return source;
    }

    public void Dispose()
    {
        Release();
        Service.Dispose();
        _release.Dispose();
    }
}
