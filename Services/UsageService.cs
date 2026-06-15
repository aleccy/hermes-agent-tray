using System.IO;
using Microsoft.Data.Sqlite;
using HermesAgentTray.Models;

namespace HermesAgentTray.Services;

public static class UsageService
{
    public static UsageSummary GetUsage(string profileName, string period = "month")
    {
        var profileHome = ProfileManager.GetProfileHome(profileName);
        var dbPath = Path.Combine(profileHome, "state.db");

        if (!File.Exists(dbPath))
            return new UsageSummary();

        var timeFilter = period switch
        {
            "day" => "date(created_at) = date('now')",
            "month" => "strftime('%Y-%m', created_at) = strftime('%Y-%m', 'now')",
            _ => "1=1"
        };

        try
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadOnly
            };

            using var conn = new SqliteConnection(builder.ConnectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                SELECT COUNT(*) as session_count,
                       COALESCE(SUM(input_tokens), 0) as total_input,
                       COALESCE(SUM(output_tokens), 0) as total_output,
                       COALESCE(SUM(estimated_cost_usd), 0) as total_cost
                FROM sessions
                WHERE {timeFilter}
                """;

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return new UsageSummary
                {
                    SessionCount = reader.GetInt32(0),
                    InputTokens = reader.GetInt64(1),
                    OutputTokens = reader.GetInt64(2),
                    EstimatedCostUsd = Math.Round(reader.GetDouble(3), 4),
                };
            }
        }
        catch { }

        return new UsageSummary();
    }
}
