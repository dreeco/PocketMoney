using Microsoft.Extensions.Logging;
using Notion.Client;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Application.Helpers;

public class NotionDatasetExporter
{
    private readonly INotionClient _client;
    private readonly ILogger _logger;

    public NotionDatasetExporter(INotionClient client, ILogger logger)
    {
        _client = client;
        _logger = logger;
    }

    /// <summary>
    /// Exporte une base de données Notion au format Markdown
    /// </summary>
    public async Task<string> ExportToMarkdownAsync(string databaseId, CancellationToken cancellationToken)
    {
        var pages = await FetchAllPagesAsync(databaseId, cancellationToken);
        if (!pages.Any()) return string.Empty;

        var headers = pages.First().Properties.Keys.ToList();
        var sb = new StringBuilder();

        // En-têtes
        sb.AppendLine($"| {string.Join(" | ", headers)} |");

        // Séparateur Markdown
        sb.AppendLine($"| {string.Join(" | ", headers.Select(_ => "---"))} |");

        // Lignes
        foreach (var page in pages)
        {
            var rowValues = headers.Select(h =>
            {
                var val = ExtractPropertyValue(page.Properties[h]);
                // Remplacer les retours à la ligne et les pipes pour ne pas casser le tableau Markdown
                return val.Replace("\n", " ").Replace("\r", "").Replace("|", "\\|");
            });
            sb.AppendLine($"| {string.Join(" | ", rowValues)} |");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Exporte une base de données Notion au format CSV
    /// </summary>
    public async Task<string> ExportToCsvAsync(string databaseId, CancellationToken cancellationToken, IEnumerable<string>? filterColumns = null, IEnumerable<string>? filterRowsName = null)
    {
        var stopWatch = new Stopwatch();
        stopWatch.Start();

        var pages = (await FetchAllPagesAsync(databaseId, cancellationToken))
            .Where(p => filterRowsName == null || filterRowsName.Contains(ExtractPropertyValue(p.Properties["Name"])));
        if (!pages.Any()) return string.Empty;

        var headers = pages.First().Properties.Keys.ToList().Where(p => filterColumns == null || filterColumns.Contains(p));
        var sb = new StringBuilder();

        // En-têtes
        sb.AppendLine(string.Join(",", headers.Select(EscapeCsvValue)));

        // Lignes
        foreach (var page in pages)
        {
            var rowValues = headers.Select(h => EscapeCsvValue(ExtractPropertyValue(page.Properties[h])));
            sb.AppendLine(string.Join(",", rowValues));
        }

        _logger.LogInformation("ExportToCsvAsync completed after {elapsedTime}ms and found {pagesCount} pages", stopWatch.ElapsedMilliseconds, pages.Count());
        return sb.ToString();
    }

    /// <summary>
    /// Récupère toutes les pages en gérant la pagination de l'API Notion (max 100 par appel)
    /// </summary>
    private async Task<List<Page>> FetchAllPagesAsync(string databaseId, CancellationToken cancellationToken)
    {
        var allPages = new List<Page>();
        string? nextCursor = null;
        bool hasMore = true;

        while (hasMore)
        {
            var queryParams = new DatabasesQueryParameters { StartCursor = nextCursor };
            var response = await _client.Databases.QueryAsync(databaseId, queryParams, cancellationToken);

            var newElements = response.Results.Select(p => (p as Page)!);
            allPages.AddRange(newElements);

            hasMore = response.HasMore;
            nextCursor = response.NextCursor;
        }

        return allPages;
    }

    /// <summary>
    /// Extrait la valeur d'une propriété Notion sous forme de chaîne de caractères simple
    /// </summary>
    private string ExtractPropertyValue(PropertyValue property)
    {
        if (property == null) return string.Empty;

        return property switch
        {
            TitlePropertyValue title => string.Join("", title.Title.Select(t => t.PlainText)),
            RichTextPropertyValue richText => string.Join("", richText.RichText.Select(t => t.PlainText)),
            NumberPropertyValue number => number.Number?.ToString() ?? string.Empty,
            SelectPropertyValue select => select.Select?.Name ?? string.Empty,
            MultiSelectPropertyValue multiSelect => string.Join(", ", multiSelect.MultiSelect.Select(s => s.Name)),
            DatePropertyValue date => date.Date?.Start?.ToString("yyyy-MM-dd") ?? string.Empty,
            CheckboxPropertyValue checkbox => checkbox.Checkbox.ToString(),
            UrlPropertyValue url => url.Url ?? string.Empty,
            EmailPropertyValue email => email.Email ?? string.Empty,
            PhoneNumberPropertyValue phone => phone.PhoneNumber ?? string.Empty,
            RelationPropertyValue relation => string.Join(", ", relation.Relation.Select(r => r.Id)),
            FormulaPropertyValue formula => ExtractFormulaValue(formula),
            _ => string.Empty // Fallback pour les types non gérés (Files, Rollups, etc.)
        };
    }

    ///// <summary>
    ///// Gère les différents types de résultats d'une formule Notion
    ///// </summary>
    //private string ExtractFormulaValue(FormulaPropertyValue formula)
    //{
    //    return formula.Formula switch
    //    {
    //        StringFormulaValue s => s.String ?? string.Empty,
    //        NumberFormulaValue n => n.Number?.ToString() ?? string.Empty,
    //        BooleanFormulaValue b => b.Boolean?.ToString() ?? string.Empty,
    //        DateFormulaValue d => d.Date?.Start?.ToString("yyyy-MM-dd") ?? string.Empty,
    //        _ => string.Empty
    //    };
    //}

    private string ExtractFormulaValue(FormulaPropertyValue formulaProperty)
    {
        var formula = formulaProperty?.Formula;
        if (formula == null) return string.Empty;

        // Dans la v4.4, on lit directement la propriété Type 
        // et on récupère la valeur correspondante.
        return formula.Type switch
        {
            "string" => formula.String ?? string.Empty,
            "number" => formula.Number?.ToString() ?? string.Empty,
            "boolean" => formula.Boolean?.ToString() ?? string.Empty,
            "date" => formula.Date?.Start?.ToString("yyyy-MM-dd") ?? string.Empty,
            _ => string.Empty
        };
    }

    /// <summary>
    /// Échappe correctement une valeur pour qu'elle ne casse pas le format CSV
    /// </summary>
    private string EscapeCsvValue(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;

        // Si la valeur contient une virgule, des guillemets ou des retours à la ligne, il faut l'encadrer de guillemets
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            // Doubler les guillemets existants pour les échapper
            value = value.Replace("\"", "\"\"");
            return $"\"{value}\"";
        }
        return value;
    }

    /// <summary>
    /// Exporte une base de données Notion au format YAML
    /// </summary>
    public async Task<string> ExportToYamlAsync(string databaseId, CancellationToken cancellationToken)
    {
        var pages = await FetchAllPagesAsync(databaseId, cancellationToken);
        if (!pages.Any()) return string.Empty;

        var headers = pages.First().Properties.Keys.ToList();
        var sb = new StringBuilder();

        foreach (var page in pages)
        {
            // Indicateur de nouvel élément dans la liste YAML
            sb.AppendLine("-");

            foreach (var header in headers)
            {
                var val = ExtractPropertyValue(page.Properties[header]);
                // Indentation de 2 espaces pour les propriétés de l'élément
                sb.AppendLine($"  {header}: {EscapeYamlValue(val)}");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Sécurise la valeur pour le YAML en l'encadrant de guillemets 
    /// et en échappant les caractères spéciaux
    /// </summary>
    private string EscapeYamlValue(string value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";

        // Échapper les antislashs, les guillemets et les retours à la ligne
        // Le formatage "JSON-like" des chaînes est nativement supporté et très sûr en YAML
        var escaped = value.Replace("\\", "\\\\")
                           .Replace("\"", "\\\"")
                           .Replace("\n", "\\n")
                           .Replace("\r", "");

        return $"\"{escaped}\"";
    }

    /// <summary>
    /// Exporte une base de données Notion au format JSON classique (Array of Objects)
    /// </summary>
    public async Task<string> ExportToJsonAsync(string databaseId, CancellationToken cancellationToken)
    {
        var pages = await FetchAllPagesAsync(databaseId, cancellationToken);
        if (!pages.Any()) return "[]";

        var headers = pages.First().Properties.Keys.ToList();
        var resultList = new List<Dictionary<string, string>>();

        foreach (var page in pages)
        {
            var row = new Dictionary<string, string>();
            foreach (var header in headers)
            {
                row[header] = ExtractPropertyValue(page.Properties[header]);
            }
            resultList.Add(row);
        }

        // WriteIndented = true pour un JSON lisible par un humain
        // WriteIndented = false pour économiser un maximum d'espaces et de sauts de ligne
        var options = new JsonSerializerOptions { WriteIndented = false };
        return JsonSerializer.Serialize(resultList, options);
    }
}