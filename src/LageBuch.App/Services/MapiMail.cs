using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LageBuch.AppLogic.Services;

namespace LageBuch.App.Services;

/// <summary>
/// Simple MAPI's <c>MAPISendMailW</c>: the one Windows API that asks the default mail program
/// (classic Outlook, Thunderbird, …) to open a new message with a file already attached. The
/// <c>mapi32.dll</c> stub ships with Windows and forwards to whichever client is registered; the
/// new Outlook registers none, which is why the caller keeps a <c>mailto:</c> fallback.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class MapiMail
{
    private const uint MapiLogonUi = 0x00000001;
    private const uint MapiDialog = 0x00000008;
    private const int SuccessSuccess = 0;
    private const int MapiUserAbort = 1;

    /// <summary>
    /// Success, or the Lagebuchführer closing the compose window unsent: either way a client took
    /// the message, so opening a second one through <c>mailto:</c> would be wrong.
    /// </summary>
    public static bool IsHandled(int result) => result is SuccessSuccess or MapiUserAbort;

    // MAPI wants a single-threaded apartment, and a client with a modal compose window (classic
    // Outlook) blocks the call until it closes -- so it gets a thread of its own, never the UI one.
    public static Task<int> SendAsync(MailDraft draft)
    {
        var result = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => Complete(result, draft)) { IsBackground = true, Name = "MAPI" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return result.Task;
    }

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Surfaces as the faulted task, which OsMailComposer turns into its mailto fallback.")]
    private static void Complete(TaskCompletionSource<int> result, MailDraft draft)
    {
        try
        {
            result.SetResult(Send(draft));
        }
        catch (Exception ex)
        {
            result.SetException(ex);
        }
    }

    private static int Send(MailDraft draft)
    {
        var file = new MapiFileDescW
        {
            Position = uint.MaxValue, // -1: attachment goes after the text, not into it
            PathName = draft.AttachmentPath,
            FileName = Path.GetFileName(draft.AttachmentPath),
        };
        var files = Marshal.AllocHGlobal(Marshal.SizeOf<MapiFileDescW>());
        try
        {
            Marshal.StructureToPtr(file, files, fDeleteOld: false);
            try
            {
                var message = new MapiMessageW
                {
                    Subject = draft.Subject,
                    NoteText = draft.Body,
                    FileCount = 1,
                    Files = files,
                };
                return (int)MAPISendMailW(IntPtr.Zero, IntPtr.Zero, ref message, MapiLogonUi | MapiDialog, 0);
            }
            finally
            {
                Marshal.DestroyStructure<MapiFileDescW>(files);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(files);
        }
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("mapi32.dll", CharSet = CharSet.Unicode)]
    private static extern uint MAPISendMailW(IntPtr session, IntPtr uiParam, ref MapiMessageW message, uint flags, uint reserved);

    // MapiMessageW and MapiFileDescW from mapi.h, field for field; ULONG is 32-bit on every Windows.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MapiMessageW
    {
        public uint Reserved;
        public string? Subject;
        public string? NoteText;
        public string? MessageType;
        public string? DateReceived;
        public string? ConversationId;
        public uint Flags;
        public IntPtr Originator;
        public uint RecipientCount;
        public IntPtr Recipients;
        public uint FileCount;
        public IntPtr Files;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MapiFileDescW
    {
        public uint Reserved;
        public uint Flags;
        public uint Position;
        public string? PathName;
        public string? FileName;
        public IntPtr FileType;
    }
}
