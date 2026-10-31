using System.Globalization;
using System.Collections;
using Microsoft.Extensions.Localization;

namespace ZARRAR.Utils;

internal class CulturedLocalizer<T>(CultureInfo cultureInfo) : IStringLocalizer<T>
{
    public LocalizedString this[string name]
    {
        get
        {
            var str = Resources.Program.ResourceManager.GetString(name, cultureInfo);
            return new(name, str ?? name, str is null);
        }
    }

    public LocalizedString this[string name, params object[] arguments]
    {
        get
        {
            var str = Resources.Program.ResourceManager.GetString(name, cultureInfo);
            return new(name, string.Format(str ?? name, arguments), str is null);
        }
    }

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
    {
        var resources = Resources.Program.ResourceManager.GetResourceSet(
            cultureInfo, createIfNotExists: true, tryParents: includeParentCultures);
        if (resources is null)
            return [];

        return resources.Cast<DictionaryEntry>()
            .Where(entry => entry.Key is string)
            .Select(entry => new LocalizedString((string)entry.Key, entry.Value?.ToString() ?? string.Empty))
            .ToArray();
    }
}
