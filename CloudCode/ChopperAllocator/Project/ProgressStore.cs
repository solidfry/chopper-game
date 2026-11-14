using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;
using Unity.Services.CloudSave.Model;

namespace ChopperAllocator;

/// <summary>
/// The players' progression record in Cloud Save, protected player data (plan section 6a-1, step 4): only a server
/// authority can write it. The game server (a service account, so the call has no player id) reads a player's record
/// when they join and writes it when a match ends; the game server works out awards and XP with the game's own code,
/// so there is no second copy of the rules here. A player can only read their own record.
/// Written without a compiler (14 Nov 2026): build it before the first deploy.
/// </summary>
public sealed class ProgressStore(IGameApiClient gameApi, ILogger<ProgressStore> logger)
{
    /// <summary>Cloud Save key of the record (the game's PlayerProfile as JSON text).</summary>
    private const string ProfileKey = "profile";
    /// <summary>Largest record accepted (characters). The real one is a few thousand.</summary>
    private const int MaxProfileLength = 60000;

    /// <summary>Game server only: the record of <paramref name="playerId"/>, or "" when there is none.</summary>
    [CloudCodeFunction("Progress_Read")]
    public async Task<string> Read(IExecutionContext context, string playerId)
    {
        RequireServer(context);
        return await Load(context, playerId);
    }

    /// <summary>Game server only: replaces the record of <paramref name="playerId"/> after match <paramref name="matchId"/>.</summary>
    [CloudCodeFunction("Progress_Write")]
    public async Task<string> Write(IExecutionContext context, string playerId, string matchId, string profile)
    {
        RequireServer(context);
        if (string.IsNullOrWhiteSpace(playerId)) throw new ArgumentException("No player id.");
        if (string.IsNullOrEmpty(profile) || profile.Length > MaxProfileLength) throw new ArgumentException("No record, or it is too large.");
        await gameApi.CloudSaveData.SetProtectedItemAsync(context, context.ServiceToken, context.ProjectId, playerId,
            new SetItemBody(ProfileKey, profile));
        logger.LogInformation("Progress written for {Player} after match {Match}", playerId, matchId);
        return "ok";
    }

    /// <summary>A player: their own record, or "" when there is none.</summary>
    [CloudCodeFunction("Progress_Mine")]
    public async Task<string> Mine(IExecutionContext context)
    {
        if (string.IsNullOrEmpty(context.PlayerId)) throw new InvalidOperationException("Only a signed-in player can call this.");
        return await Load(context, context.PlayerId);
    }

    /// <summary>A call from a player has a player id; a call from the game server's service account has none.</summary>
    private static void RequireServer(IExecutionContext context)
    {
        if (!string.IsNullOrEmpty(context.PlayerId)) throw new UnauthorizedAccessException("Only the game server can call this.");
    }

    private async Task<string> Load(IExecutionContext context, string playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId)) return "";
        var reply = await gameApi.CloudSaveData.GetProtectedItemsAsync(context, context.ServiceToken, context.ProjectId, playerId,
            new List<string> { ProfileKey });
        var items = reply.Data.Results;
        if (items == null || items.Count == 0) return "";
        return items[0].Value?.ToString() ?? "";
    }
}
