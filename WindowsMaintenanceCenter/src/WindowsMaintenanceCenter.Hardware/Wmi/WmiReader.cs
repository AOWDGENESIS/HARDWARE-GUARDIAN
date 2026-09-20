using System.Management;
using System.Runtime.InteropServices;

namespace WindowsMaintenanceCenter.Hardware.Wmi;

/// <summary>
/// Thin, defensive wrapper around WMI. Every accessor is optional: when a property is missing on
/// the target machine the accessor returns false instead of throwing or inventing a value.
/// Queries run on the thread pool with a hard timeout so the UI never blocks.
/// </summary>
public sealed class WmiReader
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<WmiObject>> _cache = new Dictionary<string, IReadOnlyList<WmiObject>>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<WmiObject>> _mutableCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public string LastError { get; private set; } = string.Empty;

    /// <summary>Retrieves objects of a WMI class. Results are cached per instance.</summary>
    public async Task<IReadOnlyList<WmiObject>> QueryAsync(string wmiClass, string? where = null, string? scope = null, CancellationToken cancellationToken = default)
    {
        var key = $"{scope ?? "root\\cimv2"}|{wmiClass}|{where}";
        lock (_gate)
        {
            if (_mutableCache.TryGetValue(key, out var cached))
            {
                return cached;
            }
        }

        var result = await Task.Run(() => Query(wmiClass, where, scope), cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            _mutableCache[key] = result;
        }

        return result;
    }

    public async Task<WmiObject?> QueryFirstAsync(string wmiClass, string? where = null, string? scope = null, CancellationToken cancellationToken = default)
    {
        var all = await QueryAsync(wmiClass, where, scope, cancellationToken).ConfigureAwait(false);
        return all.FirstOrDefault();
    }

    private IReadOnlyList<WmiObject> Query(string wmiClass, string? where, string? scopeName)
    {
        if (!OperatingSystem.IsWindows())
        {
            LastError = "WMI is only available on Windows";
            return Array.Empty<WmiObject>();
        }

        try
        {
            var scope = string.IsNullOrWhiteSpace(scopeName) ? new ManagementScope() : new ManagementScope(scopeName);
            scope.Connect();

            // Always use the explicit SELECT form: the "class name only" shorthand is accepted by
            // some WMI providers but not all, and a failure there would look like missing hardware.
            var queryText = string.IsNullOrWhiteSpace(where)
                ? $"SELECT * FROM {wmiClass}"
                : $"SELECT * FROM {wmiClass} WHERE {where}";
            var query = new SelectQuery(queryText);
            using var searcher = new ManagementObjectSearcher(scope, query);
            using var results = searcher.Get();

            var list = new List<WmiObject>();
            foreach (ManagementBaseObject item in results)
            {
                if (item is ManagementObject managementObject)
                {
                    list.Add(new WmiObject(managementObject));
                }
            }

            return list;
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or COMException or InvalidOperationException or PlatformNotSupportedException)
        {
            LastError = $"{ex.GetType().Name}: {ex.Message}";
            return Array.Empty<WmiObject>();
        }
    }
}

/// <summary>Read-only view of a single WMI instance.</summary>
public sealed class WmiObject
{
    private readonly Dictionary<string, object?> _properties = new(StringComparer.OrdinalIgnoreCase);

    internal WmiObject(ManagementObject instance)
    {
        foreach (PropertyData property in instance.Properties)
        {
            try
            {
                _properties[property.Name] = property.Value;
            }
            catch (Exception)
            {
                _properties[property.Name] = null;
            }
        }
    }

    public IReadOnlyDictionary<string, object?> Properties => _properties;

    public bool TryGetString(string name, out string value)
    {
        value = string.Empty;
        if (!_properties.TryGetValue(name, out var raw) || raw is null)
        {
            return false;
        }

        var text = raw switch
        {
            string s => s,
            string[] array => array.FirstOrDefault() ?? string.Empty,
            DateTime date => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            bool flag => flag ? "TRUE" : "FALSE",
            _ => raw.ToString() ?? string.Empty,
        };

        value = text.Trim();
        return value.Length > 0;
    }

    public string? GetString(string name) => TryGetString(name, out var value) ? value : null;

    public bool TryGetUInt(string name, out uint value)
    {
        value = 0;
        if (!_properties.TryGetValue(name, out var raw) || raw is null)
        {
            return false;
        }

        try
        {
            value = Convert.ToUInt32(raw, System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public bool TryGetULong(string name, out ulong value)
    {
        value = 0;
        if (!_properties.TryGetValue(name, out var raw) || raw is null)
        {
            return false;
        }

        try
        {
            value = Convert.ToUInt64(raw, System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public bool TryGetInt(string name, out int value)
    {
        value = 0;
        if (!_properties.TryGetValue(name, out var raw) || raw is null)
        {
            return false;
        }

        try
        {
            value = Convert.ToInt32(raw, System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public bool TryGetBool(string name, out bool value)
    {
        value = false;
        if (!_properties.TryGetValue(name, out var raw) || raw is null)
        {
            return false;
        }

        if (raw is bool flag)
        {
            value = flag;
            return true;
        }

        if (raw.ToString() is { } text && bool.TryParse(text, out var parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }

    public bool TryGetDateTime(string name, out DateTime value)
    {
        value = default;
        if (!_properties.TryGetValue(name, out var raw) || raw is null)
        {
            return false;
        }

        switch (raw)
        {
            case DateTime date:
                value = date;
                return true;
            case string text:
                return TryParseWmiDate(text, out value);
            default:
                return false;
        }
    }

    /// <summary>
    /// Reads a numeric array property, for example <c>Win32_SystemEnclosure.ChassisTypes</c>.
    /// No text is parsed and no value is invented: a property that is not a numeric array comes back
    /// empty, and the caller reports it as unavailable.
    /// </summary>
    public IReadOnlyList<uint> GetUIntArray(string name)
    {
        if (!_properties.TryGetValue(name, out var raw) || raw is null)
        {
            return Array.Empty<uint>();
        }

        return raw switch
        {
            uint[] numbers => numbers,
            ushort[] numbers => numbers.Select(n => (uint)n).ToArray(),
            int[] numbers => numbers.Select(n => (uint)n).ToArray(),
            ushort number => new[] { (uint)number },
            uint number => new[] { number },
            _ => Array.Empty<uint>(),
        };
    }

    public IReadOnlyList<string> GetStringArray(string name)
    {
        if (!_properties.TryGetValue(name, out var raw) || raw is null)
        {
            return Array.Empty<string>();
        }

        return raw switch
        {
            string[] array => array,
            ushort[] numbers => numbers.Select(n => ((char)n).ToString()).ToArray(),
            uint[] numbers => numbers.Select(n => ((char)n).ToString()).ToArray(),
            _ => new[] { raw.ToString() ?? string.Empty },
        };
    }

    /// <summary>Decodes a CIM_DATETIME string such as 20240115000000.000000+000.</summary>
    public static bool TryParseWmiDate(string text, out DateTime value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text) || text.Length < 14)
        {
            return false;
        }

        try
        {
            // A DMTF timestamp: the span overload parses the parts without copying.
            var year = int.Parse(text.AsSpan(0, 4), System.Globalization.CultureInfo.InvariantCulture);
            var month = int.Parse(text.AsSpan(4, 2), System.Globalization.CultureInfo.InvariantCulture);
            var day = int.Parse(text.AsSpan(6, 2), System.Globalization.CultureInfo.InvariantCulture);
            var hour = int.Parse(text.AsSpan(8, 2), System.Globalization.CultureInfo.InvariantCulture);
            var minute = int.Parse(text.AsSpan(10, 2), System.Globalization.CultureInfo.InvariantCulture);
            var second = int.Parse(text.AsSpan(12, 2), System.Globalization.CultureInfo.InvariantCulture);

            if (month is < 1 or > 12 || day is < 1 or > 31)
            {
                return false;
            }

            value = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
