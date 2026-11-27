using JobHuntHQ.Services;

namespace JobHuntHQ.Tests;

public class CsvParserTests
{
    [Fact]
    public void Parse_reads_unquoted_rows_and_skips_blank_lines()
    {
        var records = CsvParser.Parse("Company,Title\n\nAcme,Engineer\n");

        Assert.Equal(2, records.Count);
        Assert.Equal(1, records[0].LineNumber);
        Assert.Equal(new[] { "Company", "Title" }, records[0].Fields);
        Assert.Equal(3, records[1].LineNumber);
        Assert.Equal(new[] { "Acme", "Engineer" }, records[1].Fields);
    }

    [Fact]
    public void Parse_keeps_commas_newlines_and_quotes_inside_quoted_fields()
    {
        var text = "Company,Notes\r\n\"Acme, Inc.\",\"line one\r\nline \"\"two\"\"\"\r\n";
        var records = CsvParser.Parse(text);

        Assert.Equal(2, records.Count);
        Assert.Equal(new[] { "Company", "Notes" }, records[0].Fields);
        Assert.Equal(2, records[1].LineNumber);
        Assert.Equal("Acme, Inc.", records[1].Fields[0]);
        Assert.Equal("line one\r\nline \"two\"", records[1].Fields[1]);
    }

    [Fact]
    public void Parse_flushes_a_final_record_without_a_trailing_newline()
    {
        var records = CsvParser.Parse("only,row");

        Assert.Single(records);
        Assert.Equal(1, records[0].LineNumber);
        Assert.Equal(new[] { "only", "row" }, records[0].Fields);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Parse_returns_no_records_for_empty_input(string text)
    {
        Assert.Empty(CsvParser.Parse(text));
    }
}
