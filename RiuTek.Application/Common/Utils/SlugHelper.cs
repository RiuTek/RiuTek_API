using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RiuTek.Application.Common.Utils;

public static class SlugHelper
{
    public static string GenerateSlug(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Convert to lowercase
        text = text.ToLowerInvariant().Trim();

        // Handle specific Vietnamese character 'đ' / 'Đ'
        text = text.Replace("đ", "d");

        // Remove diacritics / accents
        var normalizedString = text.Normalize(NormalizationForm.FormD);
        var stringBuilder = new StringBuilder();

        foreach (var c in normalizedString)
        {
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != UnicodeCategory.NonSpacingMark)
            {
                stringBuilder.Append(c);
            }
        }

        var cleanText = stringBuilder.ToString().Normalize(NormalizationForm.FormC);

        // Replace invalid chars with hyphens
        cleanText = Regex.Replace(cleanText, @"[^a-z0-9\s-]", "");

        // Convert multiple spaces/hyphens into single hyphen
        cleanText = Regex.Replace(cleanText, @"[\s-]+", " ").Trim();
        cleanText = Regex.Replace(cleanText, @"\s", "-");

        return cleanText;
    }
}
