using System.Text;

namespace pswitch;

public class TextFile(string filePath, Encoding encoding, string text)
{
    public string Text { get; set; } = text;

    public static TextFile Read(string filePath)
    {
        var hasFileBom = HasFileBoom(filePath);

        using var reader = new StreamReader(filePath, true);  // 'true' enables BOM detection

        var text = reader.ReadToEnd();
        var encoding = reader.CurrentEncoding;

        if (encoding is UTF8Encoding && !hasFileBom)
        {
            encoding = new UTF8Encoding(false);  // Ensure no BOM is written for UTF-8
        }

        return new TextFile(filePath, encoding, text);
    }

    public void Write(string text)
    {
        using var writer = new StreamWriter(filePath, false, encoding);
        writer.Write(text);

    }

    public void Write()
    {
        using var writer = new StreamWriter(filePath, false, encoding);
        writer.Write(Text);
    }


    static bool HasFileBoom(string filePath)
    {
        byte[] bom = new byte[4];  // BOM could be up to 4 bytes
        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
        {
            fs.Read(bom, 0, 4);
        }

        return bom switch
        {
        [0xEF, 0xBB, 0xBF, ..] => true,
        [0xFF, 0xFE, 0x00, 0x00] => true,
        [0x00, 0x00, 0xFE, 0xFF] => true,
        [0xFF, 0xFE, ..] => true,
        [0xFE, 0xFF, ..] => true,
            _ => false
        };
    }
}
