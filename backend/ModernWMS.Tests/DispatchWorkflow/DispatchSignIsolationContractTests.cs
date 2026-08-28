namespace ModernWMS.Tests.DispatchWorkflow;

public sealed class DispatchSignIsolationContractTests
{
    [Fact]
    public void Signing_is_a_single_local_transaction_without_remote_notification_state_transitions()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "backend", "ModernWMS.WMS", "Services", "DispatchWorkflow",
            "DispatchWorkflowService.Outbound.cs"));
        var methodStart = source.IndexOf("public async Task<SignDispatchOrderResult> SignAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private async Task<OutboundCommandResult> ExecuteOutboundMutationAsync", StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && methodEnd > methodStart, "SignAsync source block was not found.");
        var sign = source[methodStart..methodEnd];

        Assert.Contains("BeginTransactionAsync(IsolationLevel.Serializable", sign, StringComparison.Ordinal);
        Assert.Contains("order.row_version++;", sign, StringComparison.Ordinal);
        Assert.Contains("order.notification_status = DispatchSignNotificationStatus.None;", sign, StringComparison.Ordinal);
        Assert.Contains("order.notification_attempt_count = 0;", sign, StringComparison.Ordinal);
        Assert.Contains("order.notification_sent_at = null;", sign, StringComparison.Ordinal);
        Assert.Contains("order.notification_last_error = string.Empty;", sign, StringComparison.Ordinal);
        Assert.Contains("order.notification_updated_at = null;", sign, StringComparison.Ordinal);
        Assert.Contains("await InsertOperationAsync(", sign, StringComparison.Ordinal);
        Assert.Contains("if (order.damaged_qty != request.damaged_qty)", sign, StringComparison.Ordinal);
        Assert.Contains("throw DispatchWorkflowCommandException.IdempotencyConflict();", sign, StringComparison.Ordinal);
        Assert.Contains("if (previous == null && order.row_version != request.row_version)", sign, StringComparison.Ordinal);
        Assert.Contains("await transaction.CommitAsync(cancellationToken);", sign, StringComparison.Ordinal);
        Assert.Contains("await transaction.RollbackAsync(CancellationToken.None)", sign, StringComparison.Ordinal);
        Assert.DoesNotContain("NotificationClient", sign, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("HttpClient", sign, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "backend", "ModernWMS.WMS")))
            directory = directory.Parent;
        if (directory is null) throw new DirectoryNotFoundException("ModernWMS repository root not found");
        return Path.Combine([directory.FullName, .. segments]);
    }
}
