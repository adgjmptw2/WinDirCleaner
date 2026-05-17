using System.IO;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;

namespace WinDirCleaner.App.Services;

public sealed class FolderPickerService
{
    public string? PickFolder(Window? owner, string? initialPath = null)
    {
        try
        {
            using var dialog = new FolderBrowserDialog
            {
                UseDescriptionForTitle = true,
                Description = "분석할 폴더를 선택합니다. 선택만 하며 자동으로 분석하지 않습니다.",
            };

            if (!string.IsNullOrWhiteSpace(initialPath))
            {
                var trimmed = initialPath.Trim();
                if (Directory.Exists(trimmed))
                {
                    dialog.SelectedPath = trimmed;
                }
            }

            DialogResult result;
            if (owner is not null)
            {
                var handle = new WindowInteropHelper(owner).Handle;
                result = handle != IntPtr.Zero
                    ? dialog.ShowDialog(new Win32WindowOwner(handle))
                    : dialog.ShowDialog();
            }
            else
            {
                result = dialog.ShowDialog();
            }

            if (result != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
            {
                return null;
            }

            return dialog.SelectedPath.Trim();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private sealed class Win32WindowOwner : System.Windows.Forms.IWin32Window
    {
        public Win32WindowOwner(IntPtr handle) => Handle = handle;

        public IntPtr Handle { get; }
    }
}