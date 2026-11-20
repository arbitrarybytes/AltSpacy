using System.IO;
using System.Text.RegularExpressions;

namespace AltSpacy.Tests;

public class LogoAndVersionTests
{
    private static T OnSta<T>(Func<T> action)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { result = action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw error;
        return result;
    }

    [Fact]
    public void Writes_valid_multi_size_ico()
    {
        var bytes = OnSta(() =>
        {
            using var stream = new MemoryStream();
            Logo.WriteIco(stream, Logo.IcoSizes);
            return stream.ToArray();
        });

        using var reader = new BinaryReader(new MemoryStream(bytes));
        Assert.Equal(0, reader.ReadInt16());
        Assert.Equal(1, reader.ReadInt16());
        Assert.Equal(Logo.IcoSizes.Length, reader.ReadInt16());

        foreach (var size in Logo.IcoSizes)
        {
            Assert.Equal(size >= 256 ? 0 : size, reader.ReadByte());
            reader.BaseStream.Seek(7, SeekOrigin.Current);
            var length = reader.ReadInt32();
            var offset = reader.ReadInt32();
            Assert.True(offset + length <= bytes.Length);
            Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], bytes[offset..(offset + 4)]);
        }
    }

    [Fact]
    public void Renders_logo_at_requested_size() =>
        Assert.Equal(48, OnSta(() => Logo.Render(48).PixelWidth));

    [Fact]
    public void Version_is_semantic() =>
        Assert.Matches(new Regex(@"^\d+\.\d+\.\d+$"), AppInfo.Version);
}
