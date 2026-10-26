using dbsh.Core.ValueObjects;

namespace dbsh.CLI.Helpers;

/// <summary>
/// Inline progress receiver passed to the engine. Unlike <see cref="Progress{T}"/>
/// it invokes the handler synchronously on the reporting thread, which keeps the
/// console rendering deterministic while a Spectre status spinner is active.
/// </summary>
public sealed class MigrationProgress : IProgress<MigrationProgressEvent>
{
    internal Action<MigrationProgressEvent>? Handler { get; set; }

    void IProgress<MigrationProgressEvent>.Report(MigrationProgressEvent value) => Handler?.Invoke(value);
}
