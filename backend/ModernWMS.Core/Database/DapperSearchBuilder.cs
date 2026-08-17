using Dapper;
using ModernWMS.Core.DynamicSearch;

namespace ModernWMS.Core.Database;

/// <summary>
/// A parameterized WHERE fragment produced from endpoint-owned field mappings.
/// </summary>
public sealed record DapperWhereClause(string Sql, DynamicParameters Parameters);

/// <summary>
/// Converts the existing search contract into safe Dapper SQL fragments.
/// </summary>
public static class DapperSearchBuilder
{
    /// <summary>
    /// Builds an AND-combined filter. Field names must be declared by the endpoint.
    /// </summary>
    public static DapperWhereClause Build(
        IEnumerable<SearchObject> filters,
        IReadOnlyDictionary<string, string> allowedColumns)
    {
        ArgumentNullException.ThrowIfNull(filters);
        ArgumentNullException.ThrowIfNull(allowedColumns);

        var clauses = new List<string>();
        var parameters = new DynamicParameters();

        foreach (var filter in filters)
        {
            if (!allowedColumns.TryGetValue(filter.Name, out var column))
            {
                throw new ArgumentException(
                    $"Search field '{filter.Name}' is not allowed.",
                    nameof(filters));
            }

            if (string.IsNullOrWhiteSpace(filter.Text))
            {
                continue;
            }

            var parameterName = $"filter{clauses.Count}";
            switch (filter.Operator)
            {
                case Operators.Equal:
                    clauses.Add($"{column} = @{parameterName}");
                    parameters.Add(parameterName, filter.Text);
                    break;
                case Operators.Contains:
                    clauses.Add($"{column} LIKE @{parameterName} ESCAPE '!'");
                    parameters.Add(parameterName, $"%{EscapeLike(filter.Text)}%");
                    break;
                case Operators.GreaterThan:
                    clauses.Add($"{column} > @{parameterName}");
                    parameters.Add(parameterName, filter.Text);
                    break;
                case Operators.GreaterThanOrEqual:
                    clauses.Add($"{column} >= @{parameterName}");
                    parameters.Add(parameterName, filter.Text);
                    break;
                case Operators.LessThan:
                    clauses.Add($"{column} < @{parameterName}");
                    parameters.Add(parameterName, filter.Text);
                    break;
                case Operators.LessThanOrEqual:
                    clauses.Add($"{column} <= @{parameterName}");
                    parameters.Add(parameterName, filter.Text);
                    break;
                default:
                    throw new ArgumentException(
                        $"Search operator '{filter.Operator}' is not supported yet.",
                        nameof(filters));
            }
        }

        return new DapperWhereClause(string.Join(" AND ", clauses), parameters);
    }

    private static string EscapeLike(string value) => value
        .Replace("!", "!!", StringComparison.Ordinal)
        .Replace("%", "!%", StringComparison.Ordinal)
        .Replace("_", "!_", StringComparison.Ordinal);
}
