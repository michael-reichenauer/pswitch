using System.Text;

namespace pswitch;

public class TextFile(string filePath, Encoding encoding, string text)
{
    public string Text { get; set; } = text;

    public static TextFile Read(string filePath)
    {
        using var reader = new StreamReader(filePath, true);  // 'true' enables BOM detection

        var encoding = reader.CurrentEncoding;
        var text = reader.ReadToEnd();

        if (encoding is UTF8Encoding utf8Encoding && utf8Encoding.Preamble.IsEmpty)
        {
            encoding = new UTF8Encoding(false);  // Ensure no BOM is written for UTF-8
        }

        return new TextFile(filePath, encoding, text);
    }

    public void Write()
    {
        using var writer = new StreamWriter(filePath, false);
        writer.Write(Text);
    }
}
