using System;
using System.Collections.Generic;

internal static class BuildDiffEntryScenarios
{
    private static int Main()
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks++;
        }

        var a = new BuildDiffEntry { Name = "a", Hash = "hash-a", Size = 1, CRC = 1 };
        var b = new BuildDiffEntry { Name = "b", Hash = "hash-b" };
        var changed = new BuildDiffEntry { Name = "a", Hash = "changed" };
        ArtifactDelta delta = ArtifactDiffer.Diff(new[] { a, b }, new[] { changed });
        Check(delta.Added.Count == 0, "Changed entries must not become added entries.");
        Check(delta.Modified.Count == 1 && ReferenceEquals(delta.Modified[0], changed), "Modified retains target entry.");
        Check(delta.Removed.Count == 1 && delta.Removed[0] == "b", "Removed stores baseline names.");

        var metadataOnly = new BuildDiffEntry { Name = "a", Hash = "hash-a", Size = 100, CRC = 42 };
        Check(ArtifactDiffer.Diff(new[] { a }, new[] { metadataOnly }).IsEmpty, "Size and CRC must not change Diff behavior.");
        Check(ArtifactDiffer.Diff(null, new[] { a }).Added.Count == 1, "Null baseline is empty.");
        Check(ArtifactDiffer.Diff(new[] { a }, null).Removed.Count == 1, "Null target is empty.");
        Check(ArtifactDiffer.Diff(null, null).IsEmpty, "Both empty sides produce no changes.");
        Check(ArtifactDiffer.Diff(new BuildDiffEntry[] { null, new BuildDiffEntry() }, null).IsEmpty, "Invalid identities are ignored.");
        Check(ArtifactDiffer.Diff(new[] { a, changed }, new[] { a }).IsEmpty, "First duplicate baseline identity wins.");
        delta = ArtifactDiffer.Diff(new[] { a }, new[] { new BuildDiffEntry { Name = "A", Hash = "hash-a" } });
        Check(delta.Added.Count == 1 && delta.Removed.Count == 1, "Name matching stays case-sensitive.");
        Check(ArtifactDiffer.Diff(new[] { a }, new[] { new BuildDiffEntry { Name = "a", Hash = "HASH-A" } }).Modified.Count == 1,
            "Hash matching stays case-sensitive.");
        Console.WriteLine($"PASS - BuildDiffEntry: {checks} assertions; production ArtifactDiffer, no Unity or filesystem calls.");
        return 0;
    }
}
