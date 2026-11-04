using unoframework.RequestHandlers;

namespace unoframework.Tests;

public class ValueConverterTests
{
    [Flags]
    public enum Permissions
    {
        None = 0,
        Read = 1,
        Write = 2,
    }

    [Theory]
    [InlineData("42", 42)]
    [InlineData("-7", -7)]
    public void Int_ParsesDecimal(string raw, int expected)
    {
        Assert.True(ValueConverter.TryConvert<int>(raw, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("0x10")]
    [InlineData("#10")]
    [InlineData("1,000")]
    [InlineData(" 1")]
    [InlineData("1.5")]
    [InlineData("99999999999")]
    [InlineData("")]
    public void Int_RejectsNonDecimalOrOverflow(string raw)
    {
        Assert.False(ValueConverter.TryConvert<int>(raw, out _));
    }

    [Fact]
    public void Long_RejectsHex()
    {
        Assert.False(ValueConverter.TryConvert<long>("#10", out _));
        Assert.False(ValueConverter.TryConvert<uint>("-1", out _));
    }

    [Fact]
    public void Double_UsesInvariantCulture()
    {
        Assert.True(ValueConverter.TryConvert<double>("1.5", out var value));
        Assert.Equal(1.5, value);
        Assert.False(ValueConverter.TryConvert<double>("1,5", out _));
    }

    [Fact]
    public void Nullable_IsSupported()
    {
        Assert.True(ValueConverter.TryConvert<int?>("3", out var value));
        Assert.Equal(3, value);
        Assert.False(ValueConverter.TryConvert<int?>("x", out _));
    }

    [Theory]
    [InlineData("monday", DayOfWeek.Monday)]
    [InlineData("Friday", DayOfWeek.Friday)]
    [InlineData("3", DayOfWeek.Wednesday)]
    public void Enum_AcceptsDefinedNamesAndValues(string raw, DayOfWeek expected)
    {
        Assert.True(ValueConverter.TryConvert<DayOfWeek>(raw, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("7")]
    [InlineData("Monday,Tuesday")]
    [InlineData("Funday")]
    public void Enum_RejectsUndefinedValuesAndCombinationsOnNonFlags(string raw)
    {
        Assert.False(ValueConverter.TryConvert<DayOfWeek>(raw, out _));
    }

    [Fact]
    public void FlagsEnum_AcceptsCombinations()
    {
        Assert.True(ValueConverter.TryConvert<Permissions>("Read,Write", out var value));
        Assert.Equal(Permissions.Read | Permissions.Write, value);
    }

    [Fact]
    public void Guid_Bool_DateTime_AreSupported()
    {
        var guid = Guid.NewGuid();
        Assert.True(ValueConverter.TryConvert<Guid>(guid.ToString(), out var g));
        Assert.Equal(guid, g);
        Assert.True(ValueConverter.TryConvert<bool>("true", out var b));
        Assert.True(b);
        Assert.True(ValueConverter.TryConvert<DateTime>("2024-02-29", out var d));
        Assert.Equal(new DateTime(2024, 2, 29), d);
        Assert.False(ValueConverter.TryConvert<Guid>("not-a-guid", out _));
    }

    [Fact]
    public void Null_IsNeverConverted()
    {
        Assert.False(ValueConverter.TryConvert<string>(null, out _));
    }
}
