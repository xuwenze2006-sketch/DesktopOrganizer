using System.Collections.Specialized;

namespace DesktopOrganizer;

internal interface IDesktopFileClipboard
{
    bool ContainsFiles { get; }
    uint Sequence { get; }
    string[] ReadFiles();
    void CopyFiles(string[] paths);
    void Paste(string targetFolder, IntPtr owner, uint expectedSequence);
}

/// <summary>与资源管理器共用 CF_HDROP；粘贴交给 Shell 处理冲突、进度和剪切语义。</summary>
internal sealed class DesktopFileClipboard : IDesktopFileClipboard
{
    public bool ContainsFiles => NativeMethods.HasClipboardFiles();
    public uint Sequence => NativeMethods.GetClipboardSequenceNumber();
    public string[] ReadFiles() => Clipboard.GetFileDropList().Cast<string>().ToArray();

    internal static DataObject CreateCopyData(string[] paths)
    {
        var files = new StringCollection();
        files.AddRange(paths);
        var data = new DataObject();
        data.SetFileDropList(files);
        // DROPEFFECT_COPY：复制不能继承剪贴板里先前的“剪切”标记。
        data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(1)));
        return data;
    }

    public void CopyFiles(string[] paths) => Clipboard.SetDataObject(CreateCopyData(paths), copy: true);
    public void Paste(string targetFolder, IntPtr owner, uint expectedSequence) =>
        NativeMethods.PasteClipboardFiles(targetFolder, owner, expectedSequence);
}
