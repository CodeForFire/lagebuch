using System.Net.Sockets;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.AppLogic.Tests;

/// <summary>
/// #655: a sharing or export failure reaches the Lagebuchführer as a fixed German sentence that
/// says what to do, never as the exception's own (usually English, usually cut-off) text.
/// </summary>
public class FailureReasonsTests
{
    private const string Raw = "Only one usage of each socket address is normally permitted";

    [Fact]
    public void A_port_already_taken_is_named_as_such_however_deep_kestrel_wraps_it()
    {
        var failure = new IOException(
            "Failed to bind to address https://[::]:5859: address already in use.",
            new InvalidOperationException(Raw, new SocketException((int)SocketError.AddressAlreadyInUse)));

        Assert.Equal(FailureReasons.SharingPortInUse, FailureReasons.ForSharing(failure));
    }

    [Theory]
    [InlineData(SocketError.NetworkUnreachable)]
    [InlineData(SocketError.AddressNotAvailable)]
    [InlineData(SocketError.NetworkDown)]
    public void Any_other_socket_failure_points_at_the_network(SocketError error)
    {
        Assert.Equal(FailureReasons.SharingNoNetwork, FailureReasons.ForSharing(new SocketException((int)error)));
    }

    [Fact]
    public void A_socket_refused_by_the_system_points_at_firewall_and_rights()
    {
        Assert.Equal(
            FailureReasons.SharingAccessDenied,
            FailureReasons.ForSharing(new SocketException((int)SocketError.AccessDenied)));
        Assert.Equal(
            FailureReasons.SharingAccessDenied,
            FailureReasons.ForSharing(new UnauthorizedAccessException(Raw)));
    }

    [Fact]
    public void An_access_denial_wrapped_in_another_failure_still_reads_as_one()
    {
        var failure = new IOException(Raw, new UnauthorizedAccessException(Raw));

        Assert.Equal(FailureReasons.SharingAccessDenied, FailureReasons.ForSharing(failure));
    }

    [Fact]
    public void An_unknown_sharing_failure_falls_back_to_a_german_sentence()
    {
        Assert.Equal(FailureReasons.SharingFailed, FailureReasons.ForSharing(new InvalidOperationException(Raw)));
    }

    [Fact]
    public void Writing_the_pdf_without_rights_says_to_pick_another_folder()
    {
        Assert.Equal(FailureReasons.ExportAccessDenied, FailureReasons.ForExport(new UnauthorizedAccessException(Raw)));
    }

    [Fact]
    public void A_missing_folder_says_so()
    {
        Assert.Equal(FailureReasons.ExportFolderMissing, FailureReasons.ForExport(new DirectoryNotFoundException(Raw)));
    }

    [Theory]
    [InlineData(unchecked((int)0x80070020))] // ERROR_SHARING_VIOLATION
    [InlineData(unchecked((int)0x80070021))] // ERROR_LOCK_VIOLATION
    public void A_pdf_still_open_in_a_viewer_says_to_close_it(int hresult)
    {
        Assert.Equal(FailureReasons.ExportFileInUse, FailureReasons.ForExport(new IOException(Raw, hresult)));
    }

    [Theory]
    [InlineData(unchecked((int)0x80070070))] // ERROR_DISK_FULL
    [InlineData(unchecked((int)0x80070027))] // ERROR_HANDLE_DISK_FULL
    [InlineData(28)] // ENOSPC
    public void A_full_disk_says_so(int hresult)
    {
        Assert.Equal(FailureReasons.ExportDiskFull, FailureReasons.ForExport(new IOException(Raw, hresult)));
    }

    [Fact]
    public void Any_other_write_failure_says_the_file_could_not_be_written()
    {
        Assert.Equal(FailureReasons.ExportWriteFailed, FailureReasons.ForExport(new IOException(Raw)));
    }

    [Fact]
    public void An_unknown_export_failure_falls_back_to_a_german_sentence()
    {
        Assert.Equal(FailureReasons.ExportFailed, FailureReasons.ForExport(new InvalidOperationException(Raw)));
    }

    [Fact]
    public void No_reason_carries_the_exceptions_own_text()
    {
        var failures = new Exception[]
        {
            new IOException(Raw, new SocketException((int)SocketError.AddressAlreadyInUse)),
            new SocketException((int)SocketError.NetworkUnreachable),
            new UnauthorizedAccessException(Raw),
            new DirectoryNotFoundException(Raw),
            new IOException(Raw),
            new InvalidOperationException(Raw),
        };

        foreach (var failure in failures)
        {
            Assert.DoesNotContain(Raw, FailureReasons.ForSharing(failure), StringComparison.Ordinal);
            Assert.DoesNotContain(Raw, FailureReasons.ForExport(failure), StringComparison.Ordinal);
        }
    }
}
