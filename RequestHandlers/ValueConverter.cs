using System.ComponentModel;
using System.Globalization;

namespace unoframework.RequestHandlers;

internal static class ValueConverter
{
    private const NumberStyles Integer = NumberStyles.AllowLeadingSign;
    private const NumberStyles Real = NumberStyles.Float;

    // Numbers are parsed strictly: decimal digits only (no hex "0x10"/"#10", no thousands separators),
    // invariant culture.
    private static readonly Dictionary<Type, Func<string, (bool Ok, object? Value)>> NumericParsers = new()
    {
        [typeof(int)] = s => (int.TryParse(s, Integer, CultureInfo.InvariantCulture, out var v), v),
        [typeof(long)] = s => (long.TryParse(s, Integer, CultureInfo.InvariantCulture, out var v), v),
        [typeof(short)] = s => (short.TryParse(s, Integer, CultureInfo.InvariantCulture, out var v), v),
        [typeof(sbyte)] = s => (sbyte.TryParse(s, Integer, CultureInfo.InvariantCulture, out var v), v),
        [typeof(byte)] = s => (byte.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var v), v),
        [typeof(ushort)] = s => (ushort.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var v), v),
        [typeof(uint)] = s => (uint.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var v), v),
        [typeof(ulong)] = s => (ulong.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var v), v),
        [typeof(float)] = s => (float.TryParse(s, Real, CultureInfo.InvariantCulture, out var v), v),
        [typeof(double)] = s => (double.TryParse(s, Real, CultureInfo.InvariantCulture, out var v), v),
        [typeof(decimal)] = s => (decimal.TryParse(s, NumberStyles.Number & ~NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var v), v),
    };

    /// <summary>
    /// Converts a raw string (query or route value) to <typeparamref name="T"/> using invariant
    /// culture. Supports strings, numbers, enums (by name, case-insensitive), Guid, DateTime, bool
    /// and Nullable&lt;T&gt;. Returns false for anything that isn't a clean, well-defined value.
    /// </summary>
    public static bool TryConvert<T>(string? raw, out T? value)
    {
        value = default;
        if (raw is null)
        {
            return false;
        }

        if (typeof(T) == typeof(string))
        {
            value = (T)(object)raw;
            return true;
        }

        var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        if (!TryConvert(raw, target, out var converted))
        {
            return false;
        }

        value = (T)converted;
        return true;
    }

    private static bool TryConvert(string raw, Type target, out object converted)
    {
        converted = null!;

        if (NumericParsers.TryGetValue(target, out var parse))
        {
            var (ok, number) = parse(raw);
            converted = number!;
            return ok;
        }

        if (target.IsEnum)
        {
            return TryParseEnum(raw, target, out converted);
        }

        try
        {
            var result = TypeDescriptor.GetConverter(target).ConvertFromInvariantString(raw);
            if (result is null)
            {
                return false;
            }

            converted = result;
            return true;
        }
        catch (Exception ex)
            when (ex is FormatException or NotSupportedException or ArgumentException or InvalidCastException or OverflowException)
        {
            return false;
        }
    }

    /// <summary>
    /// Accepts defined member names (and defined numeric values). Comma-separated combinations are
    /// only accepted for <see cref="FlagsAttribute"/> enums.
    /// </summary>
    private static bool TryParseEnum(string raw, Type target, out object converted)
    {
        converted = null!;
        var isFlags = target.IsDefined(typeof(FlagsAttribute), inherit: false);
        if (!isFlags && raw.Contains(','))
        {
            return false;
        }

        if (!Enum.TryParse(target, raw, ignoreCase: true, out var parsed) || parsed is null)
        {
            return false;
        }

        if (!isFlags && !Enum.IsDefined(target, parsed))
        {
            return false;
        }

        converted = parsed;
        return true;
    }
}
