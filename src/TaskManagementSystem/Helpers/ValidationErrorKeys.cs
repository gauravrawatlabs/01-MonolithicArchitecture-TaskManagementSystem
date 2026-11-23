using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace TaskManagementSystem.Helpers;

/// <summary>
/// Makes every validation error key look like the JSON/query field the client sent:
///   FluentValidation "DueDateUtc" → "dueDateUtc"
///   JSON binding     "$.priority" → "priority"
///   query binding    "Status"     → "status"
///   empty body       ""           → "body"
/// </summary>
public static class ValidationErrorKeys
{
    public static string Normalize(string key)
    {
        var trimmed = key.StartsWith('$') ? key.TrimStart('$').TrimStart('.') : key;
        return trimmed.Length == 0 ? "body" : JsonNamingPolicy.CamelCase.ConvertName(trimmed);
    }

    /// <summary>Converts MVC model-binding errors (ModelState) into our error dictionary format.</summary>
    public static Dictionary<string, string[]> FromModelState(ModelStateDictionary modelState) =>
        modelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .GroupBy(entry => Normalize(entry.Key))
            .ToDictionary(
                group => group.Key,
                group => group
                    .SelectMany(entry => entry.Value!.Errors)
                    .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage) ? "The input was not valid." : error.ErrorMessage)
                    .Distinct()
                    .ToArray());
}
