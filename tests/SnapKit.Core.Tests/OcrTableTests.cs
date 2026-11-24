namespace SnapKit.Tests;

public class OcrTableTests
{
    private static void AddCellWords(List<OcrWordBox> words, double startX, double y, string text, double charW = 5.5, double spaceW = 3.5, double height = 11.0)
    {
        double x = startX;
        foreach (var part in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            double w = Math.Max(4.0, part.Length * charW);
            words.Add(new OcrWordBox(part, x, y, w, height));
            x += w + spaceW;
        }
    }

    [Fact]
    public void Reconstructs_multi_column_rmm_server_table_with_tight_clipped_cells_and_icon_gap()
    {
        // Mirrors real RMM dashboard: Client | Site | Server | Description | [icons] | Last Response | Last Boot Time
        var words = new List<OcrWordBox>();
        var rows = new (string client, string site, string server, string desc, string resp, string boot)[]
        {
            ("Client", "Site", "Server", "Description", "Last Response", "Last Boot Time"),
            ("Signature Crafts", "Hawthorne, Ne...", "HV1", "hyper-v server", "Nov-24-2026 - 07:54", "71 days, 23 hrs, 34 mins ago"),
            ("Merola Tile Distributors of...", "Manalapan, NJ", "HV3", "hyperV 3 / ITPub", "Nov-24-2026 - 07:52", "2 hrs, 4 mins ago"),
            ("Merola Tile Distributors of...", "Manalapan, NJ", "HV4", "hyperV 4", "Nov-24-2026 - 07:52", "2 hrs, 4 mins ago"),
            ("Duggal Visual Studios", "NVRs", "200WH-DW-NVR", "200 Rogers NVR", "Nov-24-2026 - 08:02", "168 days, 22 hrs, 21 mins ago"),
            ("Andromeda Advantage Inc", "Skyline Restorat...", "AAI02", "SRIHV2: Sage 300", "Nov-24-2026 - 08:02", "241 days, 11 hrs, 1 min ago"),
        };

        // Note: "Merola Tile Distributors of..." is 30 chars -> ~175px wide starting at 100 -> ends at ~275,
        // only ~8px before Site at x=283! And there is a huge 260px icon gap between Description and Last Response.
        for (int r = 0; r < rows.Length; r++)
        {
            double y = 20 + r * 22;
            AddCellWords(words, 100, y, rows[r].client);
            AddCellWords(words, 283, y + (r % 2), rows[r].site);       // slight ±1px Y jitter
            AddCellWords(words, 402, y, rows[r].server);
            AddCellWords(words, 495, y - (r % 2), rows[r].desc);
            // Icon columns occupy x=620..880 (no text) -> should collapse to ONE tab, not empty columns
            AddCellWords(words, 880, y, rows[r].resp);
            AddCellWords(words, 1015, y, rows[r].boot);
        }

        // Shuffle word order to simulate WinRT OCR emitting column-by-column lines out of row order
        var shuffled = words.OrderBy(w => w.X).ThenByDescending(w => w.Y).ToList();
        var table = OcrTable.Build(shuffled);

        Assert.True(table.IsLikelyTable);
        Assert.Equal(6, table.ColumnCount);
        Assert.Equal(6, table.RowCount);

        var tsvLines = table.ToTsv().Split("\r\n");
        Assert.Equal("Client\tSite\tServer\tDescription\tLast Response\tLast Boot Time", tsvLines[0]);
        Assert.Equal("Signature Crafts\tHawthorne, Ne...\tHV1\thyper-v server\tNov-24-2026 - 07:54\t71 days, 23 hrs, 34 mins ago", tsvLines[1]);
        Assert.Equal("Merola Tile Distributors of...\tManalapan, NJ\tHV3\thyperV 3 / ITPub\tNov-24-2026 - 07:52\t2 hrs, 4 mins ago", tsvLines[2]);
        Assert.Equal("Duggal Visual Studios\tNVRs\t200WH-DW-NVR\t200 Rogers NVR\tNov-24-2026 - 08:02\t168 days, 22 hrs, 21 mins ago", tsvLines[4]);
    }

    [Fact]
    public void Does_not_split_aligned_spaces_in_repeated_prefix_rows()
    {
        // Mirrors bottom RMM Checks table: identical prefix "Drive Space Change Check - drive C/D/E:"
        var words = new List<OcrWordBox>();
        var rows = new (string desc, string info, string dt)[]
        {
            ("Drive Space Change Check - drive C:", "Total: 275.83 GB, Free: 109.31 GB", "24-Nov-2026 06:07"),
            ("Drive Space Change Check - drive D:", "Total: 1.75 TB, Free: 865.91 GB", "24-Nov-2026 06:07"),
            ("Drive Space Change Check - drive E:", "Total: 32.74 TB, Free: 6.73 TB", "24-Nov-2026 06:07"),
        };

        for (int r = 0; r < rows.Length; r++)
        {
            double y = 10 + r * 20;
            AddCellWords(words, 180, y, rows[r].desc);
            AddCellWords(words, 430, y, rows[r].info);
            AddCellWords(words, 670, y, rows[r].dt);
        }

        var table = OcrTable.Build(words);

        Assert.True(table.IsLikelyTable);
        Assert.Equal(3, table.ColumnCount);
        Assert.Equal(3, table.RowCount);
        Assert.Equal("Drive Space Change Check - drive C:", table.Rows[0][0]);
        Assert.Equal("Total: 275.83 GB, Free: 109.31 GB", table.Rows[0][1]);
        Assert.Equal("24-Nov-2026 06:07", table.Rows[0][2]);
    }

    [Fact]
    public void Preserves_empty_middle_cells_without_shifting_columns_left()
    {
        var words = new List<OcrWordBox>();
        // Row 0: A | B | C
        AddCellWords(words, 10, 10, "Name");
        AddCellWords(words, 150, 10, "Owner");
        AddCellWords(words, 290, 10, "Status");
        // Row 1: missing Owner!
        AddCellWords(words, 10, 32, "Server-01");
        AddCellWords(words, 290, 32, "Online");
        // Row 2: full row
        AddCellWords(words, 10, 54, "Server-02");
        AddCellWords(words, 150, 54, "Ops");
        AddCellWords(words, 290, 54, "Offline");

        var table = OcrTable.Build(words);

        Assert.True(table.IsLikelyTable);
        Assert.Equal(3, table.ColumnCount);
        Assert.Equal("Name\tOwner\tStatus\r\nServer-01\t\tOnline\r\nServer-02\tOps\tOffline", table.ToTsv());
    }

    [Fact]
    public void Plain_prose_paragraph_is_not_detected_as_a_table()
    {
        var words = new List<OcrWordBox>();
        AddCellWords(words, 20, 10, "SnapKit sits in the system tray and grabs a region");
        AddCellWords(words, 20, 28, "so you can mark it up before copying to clipboard.");
        AddCellWords(words, 20, 46, "Nothing is saved to disk unless you ask explicitly.");

        var table = OcrTable.Build(words);

        Assert.False(table.IsLikelyTable);
        Assert.Equal(1, table.ColumnCount);
        Assert.Equal(3, table.RowCount);
    }

    [Fact]
    public void Single_row_with_wide_gaps_splits_into_columns()
    {
        var words = new List<OcrWordBox>();
        AddCellWords(words, 10, 10, "Signature Crafts");
        AddCellWords(words, 180, 10, "HV1");
        AddCellWords(words, 280, 10, "hyper-v server");

        var table = OcrTable.Build(words);

        Assert.Equal(3, table.ColumnCount);
        Assert.Equal("Signature Crafts\tHV1\thyper-v server", table.ToTsv());
    }

    [Fact]
    public void Csv_escapes_commas_and_quotes_cleanly()
    {
        var words = new List<OcrWordBox>();
        AddCellWords(words, 10, 10, "Manalapan, NJ");
        AddCellWords(words, 180, 10, "2 hrs, 4 mins");
        AddCellWords(words, 10, 32, "Hawthorne, NJ");
        AddCellWords(words, 180, 32, "10 mins");

        var table = OcrTable.Build(words);

        Assert.Equal("\"Manalapan, NJ\",\"2 hrs, 4 mins\"\r\n\"Hawthorne, NJ\",10 mins", table.ToCsv());
    }
}
