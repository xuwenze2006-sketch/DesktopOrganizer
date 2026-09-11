// Shell 图标后台队列、占位视觉与异步缓存回填
namespace DesktopOrganizer
{
    public partial class MainWindow
    {
        private readonly ShellIconLoadService _shellIconLoadService = new();
        private readonly Dictionary<IconLoadRequestKey, List<IconVisualBinding>> _pendingIconVisuals = new();
        private readonly Dictionary<IconLoadRequestKey, int> _queuedIconLoads = new();

        private FrameworkElement CreateAsyncIconElement(
            string fullPath,
            string displayName,
            GroupInfo? parentGroup,
            double iconSize = 42,
            string? shellPlaceholder = null)
        {
            bool isShellNamespace = ShellItemLocation.TryDecode(
                fullPath,
                out _,
                out bool isShellFolder);
            bool isFolder = isShellNamespace
                ? isShellFolder
                : GetDesktopItemVisualKind(displayName, fullPath) == "file-system-folder";

            var image = new Image
            {
                Width = iconSize,
                Height = iconSize,
                Stretch = Stretch.Uniform,
                SnapsToDevicePixels = true,
                IsHitTestVisible = false
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.LowQuality);

            var placeholder = new TextBlock
            {
                Text = isShellNamespace ? shellPlaceholder ?? "🖥" : isFolder ? "📁" : "📄",
                FontSize = Math.Max(28, iconSize * 0.8),
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
                Opacity = 0.88
            };

            var host = new Grid
            {
                Width = iconSize,
                Height = iconSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            host.Children.Add(placeholder);
            host.Children.Add(image);

            string cacheKey = GetIconCacheKey(fullPath);
            if (_iconCache.TryGetValue(cacheKey, out BitmapSource? cached))
            {
                ApplyIconSource(image, placeholder, cached);
                return host;
            }

            var requestKey = new IconLoadRequestKey(
                cacheKey,
                _iconVisualGeneration,
                GetIconCacheVersion(cacheKey));
            if (!_pendingIconVisuals.TryGetValue(requestKey, out List<IconVisualBinding>? bindings))
            {
                bindings = new List<IconVisualBinding>();
                _pendingIconVisuals[requestKey] = bindings;
            }

            bindings.Add(new IconVisualBinding(image, placeholder));
            int priority = parentGroup == null
                ? 0
                : parentGroup.IsCollapsed && !IsGroupPeekActive(parentGroup) ? 2 : 1;
            if (!_queuedIconLoads.TryGetValue(requestKey, out int queuedPriority) ||
                priority < queuedPriority)
            {
                _queuedIconLoads[requestKey] = priority;
                if (!_shellIconLoadService.Enqueue(
                        requestKey,
                        fullPath,
                        priority,
                        IconLoadCompletedOnWorkerThread))
                {
                    _queuedIconLoads.Remove(requestKey);
                }
            }

            return host;
        }

        private void IconLoadCompletedOnWorkerThread(IconLoadResult result)
        {
            _diagnostics.LogSlowOperation("shell-icon-load", result.Elapsed);
            if (_isClosing || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                return;
            }

            try
            {
                _ = Dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(() => ApplyCompletedIconLoad(result)));
            }
            catch (InvalidOperationException)
            {
                // Dispatcher 已进入关闭流程；保留占位图即可。
            }
            catch (TaskCanceledException)
            {
                // Dispatcher 已进入关闭流程。
            }
        }

        private void ApplyCompletedIconLoad(IconLoadResult result)
        {
            IconLoadRequestKey requestKey = result.RequestKey;
            _queuedIconLoads.Remove(requestKey);
            _pendingIconVisuals.Remove(requestKey, out List<IconVisualBinding>? bindings);

            if (_isClosing ||
                requestKey.Generation != _iconVisualGeneration ||
                requestKey.CacheVersion != GetIconCacheVersion(requestKey.CacheKey))
            {
                return;
            }

            _iconCache[requestKey.CacheKey] = result.Source;
            if (bindings == null)
            {
                return;
            }

            foreach (IconVisualBinding binding in bindings)
            {
                if (!binding.Image.TryGetTarget(out Image? image) ||
                    !binding.Placeholder.TryGetTarget(out TextBlock? placeholder))
                {
                    continue;
                }

                ApplyIconSource(image, placeholder, result.Source);
            }
        }

        private static void ApplyIconSource(
            Image image,
            TextBlock placeholder,
            BitmapSource? source)
        {
            if (source == null)
            {
                // 失败或无图标结果不能覆盖已经显示的有效图标。这样即使第三方
                // Shell 扩展迟到返回空值，也不会让图标在真实图与占位图间闪烁。
                if (image.Source == null)
                {
                    if (image.Visibility != Visibility.Collapsed)
                    {
                        image.Visibility = Visibility.Collapsed;
                    }
                    if (placeholder.Visibility != Visibility.Visible)
                    {
                        placeholder.Visibility = Visibility.Visible;
                    }
                }
                return;
            }

            if (!ReferenceEquals(image.Source, source))
            {
                image.Source = source;
            }
            if (image.Visibility != Visibility.Visible)
            {
                image.Visibility = Visibility.Visible;
            }
            if (placeholder.Visibility != Visibility.Collapsed)
            {
                placeholder.Visibility = Visibility.Collapsed;
            }
        }

        private void AdvanceIconCacheGeneration()
        {
            _pendingIconVisuals.Clear();
            _queuedIconLoads.Clear();
            _shellIconLoadService.AdvanceGeneration(_iconVisualGeneration);
        }

        private int GetIconCacheVersion(string cacheKey) =>
            _iconCacheVersions.TryGetValue(cacheKey, out int version)
                ? version
                : 0;

        private int InvalidateIconCacheLocations(IEnumerable<string> locations)
        {
            ArgumentNullException.ThrowIfNull(locations);
            var invalidatedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string location in locations.Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                invalidatedKeys.UnionWith(IconCacheInvalidation.GetCandidateCacheKeys(location));
            }

            foreach (string cacheKey in invalidatedKeys)
            {
                _iconCache.Remove(cacheKey);
                unchecked
                {
                    _iconCacheVersions[cacheKey] = GetIconCacheVersion(cacheKey) + 1;
                }
            }

            foreach (IconLoadRequestKey requestKey in _pendingIconVisuals.Keys
                         .Where(key => invalidatedKeys.Contains(key.CacheKey))
                         .ToList())
            {
                _pendingIconVisuals.Remove(requestKey);
            }

            foreach (IconLoadRequestKey requestKey in _queuedIconLoads.Keys
                         .Where(key => invalidatedKeys.Contains(key.CacheKey))
                         .ToList())
            {
                _queuedIconLoads.Remove(requestKey);
            }

            return invalidatedKeys.Count;
        }

        private void StopAsyncIconLoading()
        {
            _pendingIconVisuals.Clear();
            _queuedIconLoads.Clear();
            _shellIconLoadService.Dispose();
        }

        private sealed class IconVisualBinding
        {
            public IconVisualBinding(Image image, TextBlock placeholder)
            {
                Image = new WeakReference<Image>(image);
                Placeholder = new WeakReference<TextBlock>(placeholder);
            }

            public WeakReference<Image> Image { get; }

            public WeakReference<TextBlock> Placeholder { get; }
        }
    }
}
