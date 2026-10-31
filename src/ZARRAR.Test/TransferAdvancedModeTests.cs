using System.Security.Cryptography;
using System.Text;
using ZARRAR.Models.Data;
using ZARRAR.Models.Transfer;
using ZARRAR.Utils;
using Xunit;

namespace ZARRAR.Test;

public sealed class TransferAdvancedModeTests
{
    [Fact]
    public void RemoteAttachmentExportRoundTripUsesUrlWithoutSyntheticFileName()
    {
        var challenge = new GameChallenge
        {
            Id = 8,
            Title = "Portable remote attachment",
            Type = ChallengeType.StaticAttachment,
            Attachment = new Attachment
            {
                Type = FileType.Remote,
                RemoteUrl = "https://example.invalid/harmless.txt"
            }
        };

        var json = TransferHelper.ToJson(challenge.ToTransfer());
        var transfer = TransferHelper.FromJson<TransferChallenge>(json)!;

        TransferValidator.ValidateRecursive(transfer, "challenge");

        Assert.Equal(FileType.Remote, transfer.Attachment?.Type);
        Assert.Equal("https://example.invalid/harmless.txt", transfer.Attachment?.RemoteUrl);
        Assert.Null(transfer.Attachment?.FileName);
    }

    [Fact]
    public void TransferRoundTripPreservesDefinitionsWithoutRuntimeAssignments()
    {
        const string manifest = "{\"schemaVersion\":1,\"entrypoint\":\"/builder/run\",\"network\":false}";
        var template = new VariantTemplate
        {
            Id = 31,
            ChallengeId = 7,
            Version = "2026.09",
            Kind = VariantTemplateKind.Reverse,
            ManifestJson = manifest,
            ManifestHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(manifest))),
            BuilderImageDigest = "registry.example/builder@sha256:" + new string('a', 64),
            OutputFileName = "challenge.bin",
            Status = VariantTemplateStatus.Validated,
            ValidatedAtUtc = DateTimeOffset.UtcNow
        };
        var flag = new FlagContext { Id = 41, ChallengeId = 7, Flag = "ZARRAR{portable}" };
        var challenge = new GameChallenge
        {
            Id = 7,
            Title = "Portable mode",
            Type = ChallengeType.DynamicAttachment,
            Flags = [flag],
            SharedRuntime = new SharedRuntimeConfiguration
            {
                ChallengeId = 7,
                Origin = "https://runtime.example/",
                Audience = "portable-runtime",
                StateMode = SharedRuntimeStateMode.TeamNamespaced,
                MaxConcurrentSessions = 50
            },
            VariantTemplates = [template],
            VariantDatasetItems =
            [
                new VariantDatasetItem
                {
                    ChallengeId = 7,
                    FlagContextId = 41,
                    DatasetKey = "dataset-41",
                    IsEnabled = true,
                    TemplateId = 31,
                    Template = template
                }
            ]
        };

        var json = TransferHelper.ToJson(challenge.ToTransfer());
        var transfer = TransferHelper.FromJson<TransferChallenge>(json)!;
        TransferValidator.ValidateRecursive(transfer, "challenge");

        Assert.Equal("https://runtime.example/", transfer.SharedRuntime?.Origin);
        Assert.Equal(SharedRuntimeStateMode.TeamNamespaced, transfer.SharedRuntime?.StateMode);
        Assert.Equal("2026.09", Assert.Single(transfer.VariantTemplates!).Version);
        var portableFlag = Assert.Single(transfer.Flags.Static!);
        Assert.Equal("dataset-41", portableFlag.DatasetKey);
        Assert.Equal("2026.09", portableFlag.VariantTemplateVersion);
        Assert.DoesNotContain("validatedAtUtc", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("selectedParticipationId", json, StringComparison.OrdinalIgnoreCase);
    }
}
