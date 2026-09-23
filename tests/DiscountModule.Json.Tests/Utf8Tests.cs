using System.Text;

using DiscountModule.Json;
using DiscountModule.Models;

namespace DiscountModule.Json.Tests;

/// <summary>
/// An order arrives as bytes (a file, standard input, an HTTP body) and must be UTF-8. Bytes that are not
/// UTF-8 are reported; they are never replaced with U+FFFD and priced.
/// </summary>
public class Utf8Tests
{
    // The JSON around the item name, so that a test can put any bytes into the name.
    private const string BeforeName = "{ \"items\": [ { \"name\": \"";
    private const string AfterName = "\", \"category\": \"Clothing\", \"unitPrice\": 350 } ] }";

    private static byte[] OrderWithName(params byte[] name)
        => [.. Encoding.UTF8.GetBytes(BeforeName), .. name, .. Encoding.UTF8.GetBytes(AfterName)];

    private static byte[] Utf16Order(Encoding utf16)
        => [.. utf16.GetPreamble(), .. utf16.GetBytes(BeforeName + "Hat" + AfterName)];

    private static InputFormatException ParseUtf8Fails(byte[] bytes)
        => Assert.Throws<InputFormatException>(() => OrderRequestParser.ParseUtf8(bytes));

    private static InputFormatException ReadFails(byte[] bytes)
    {
        using TextReader reader = OrderRequestParser.CreateUtf8Reader(new MemoryStream(bytes));
        return Assert.Throws<InputFormatException>(() => OrderRequestParser.Read(reader));
    }

    [Fact]
    public void Utf8_bytes_are_read_with_their_thai_text()
    {
        OrderRequest request = OrderRequestParser.ParseUtf8(OrderWithName(Encoding.UTF8.GetBytes("เสื้อยืด")));

        Assert.Equal("เสื้อยืด", Assert.Single(request.Cart.Items).Name);
    }

    [Fact]
    public void A_utf8_byte_order_mark_is_skipped()
    {
        OrderRequest request = OrderRequestParser.ParseUtf8([.. Encoding.UTF8.GetPreamble(), .. OrderWithName(Encoding.UTF8.GetBytes("Hat"))]);

        Assert.Equal("Hat", Assert.Single(request.Cart.Items).Name);
    }

    [Fact]
    public void Bytes_that_are_only_a_byte_order_mark_are_an_empty_input()
    {
        Assert.Equal(
            "Input is empty; expected a JSON object with \"items\" and \"campaigns\"",
            Assert.Single(ParseUtf8Fails([.. Encoding.UTF8.GetPreamble()]).Errors));
    }

    [Theory]
    [InlineData(new byte[] { 0xFF }, "byte 0xFF")] // a byte UTF-8 never uses
    [InlineData(new byte[] { 0xC0, 0xAF }, "byte 0xC0")] // "/" written in two bytes instead of one
    [InlineData(new byte[] { 0xED, 0xA0, 0x80 }, "byte 0xED")] // half a surrogate pair, encoded on its own
    [InlineData(new byte[] { 0xF4, 0x90, 0x80, 0x80 }, "byte 0xF4")] // above U+10FFFF, the last code point
    [InlineData(new byte[] { 0xE0, 0xB8 }, "bytes 0xE0 0xB8")] // a Thai letter cut short: 2 of its 3 bytes
    public void Bytes_that_are_not_utf8_are_reported_with_their_offset(byte[] name, string reported)
    {
        InputFormatException error = ParseUtf8Fails(OrderWithName(name));

        Assert.Equal($"Input is not valid UTF-8 text ({reported} at offset {BeforeName.Length})", Assert.Single(error.Errors));
    }

    [Theory]
    [InlineData(false)] // little endian, what "> file" in Windows PowerShell 5.1 writes
    [InlineData(true)]
    public void Utf16_text_is_reported_by_its_byte_order_mark(bool bigEndian)
    {
        byte[] bytes = Utf16Order(bigEndian ? Encoding.BigEndianUnicode : Encoding.Unicode);

        Assert.Equal(
            "Input is UTF-16 text (it starts with a UTF-16 byte order mark); it must be UTF-8",
            Assert.Single(ParseUtf8Fails(bytes).Errors));
    }

    [Fact]
    public void Text_that_starts_with_a_byte_order_mark_character_is_read()
    {
        // U+FEFF is built from its number: an editor would show nothing where it is.
        string json = (char)0xFEFF + BeforeName + "Hat" + AfterName;

        Assert.Equal("Hat", Assert.Single(OrderRequestParser.Parse(json).Cart.Items).Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_utf8_reader_reads_utf8_with_or_without_a_byte_order_mark(bool withByteOrderMark)
    {
        byte[] order = OrderWithName(Encoding.UTF8.GetBytes("เสื้อยืด"));
        using TextReader reader = OrderRequestParser.CreateUtf8Reader(
            new MemoryStream(withByteOrderMark ? [.. Encoding.UTF8.GetPreamble(), .. order] : order));

        Assert.Equal("เสื้อยืด", Assert.Single(OrderRequestParser.Read(reader).Cart.Items).Name);
    }

    [Fact]
    public void A_utf8_reader_reports_a_byte_that_is_not_utf8()
    {
        Assert.Equal("Input is not valid UTF-8 text (byte 0xFF)", Assert.Single(ReadFails(OrderWithName(0xFF)).Errors));
    }

    [Fact]
    public void A_utf8_reader_does_not_switch_to_utf16_on_a_byte_order_mark()
    {
        Assert.Equal("Input is not valid UTF-8 text (byte 0xFF)", Assert.Single(ReadFails(Utf16Order(Encoding.Unicode)).Errors));
    }
}
