using System.Globalization;
using System.Text;

namespace Cartagena.Api.Services;

/// <summary>PDF simple y autosuficiente para tickets/reporte; nunca imprime PAN/CVV.</summary>
public sealed class PdfService
{
    public byte[] Create(string title,IEnumerable<string> lines)
    {
        var source = new List<string>{title};
        source.AddRange(lines);
        var chunks=source.Chunk(42).ToList();
        var pageCount=chunks.Count;
        var max=3+2*pageCount;
        var objects=new string[max+1];
        objects[1]="<< /Type /Catalog /Pages 2 0 R >>";
        objects[2]="<< /Type /Pages /Kids ["+string.Join(" ",Enumerable.Range(0,pageCount).Select(i=>$"{4+2*i} 0 R"))+$"] /Count {pageCount} >>";
        objects[3]="<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>";
        for(var i=0;i<pageCount;i++)
        {
            var page=4+2*i; var content=page+1;
            objects[page]=$"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 3 0 R >> >> /Contents {content} 0 R >>";
            var text=new StringBuilder("BT /F1 11 Tf 40 800 Td 15 TL\n");
            foreach(var line in chunks[i])
            {
                var safe = Normalize(line.Length>95?line[..95]:line);
                text.Append('(').Append(safe).Append(") Tj T*\n");
            }
            text.Append("ET\n");
            var stream=text.ToString();
            var len=Encoding.ASCII.GetByteCount(stream);
            objects[content]=$"<< /Length {len} >>\nstream\n{stream}endstream";
        }
        using var output=new MemoryStream();
        void Write(string str)
        {
            var bytes=Encoding.ASCII.GetBytes(str);
            output.Write(bytes,0,bytes.Length);
        }
        Write("%PDF-1.4\n");
        var offsets=new long[max+1];
        for(var i=1;i<=max;i++)
        {
            offsets[i]=output.Position;
            Write($"{i} 0 obj\n{objects[i]}\nendobj\n");
        }
        var start=output.Position;
        Write($"xref\n0 {max+1}\n0000000000 65535 f \n");
        for(var i=1;i<=max;i++) Write($"{offsets[i]:D10} 00000 n \n");
        Write($"trailer\n<< /Size {max+1} /Root 1 0 R >>\nstartxref\n{start}\n%%EOF\n");
        return output.ToArray();
    }

    private static string Normalize(string input)
    {
        var str=input.Normalize(NormalizationForm.FormD);
        var buffer=new StringBuilder();
        foreach(var ch in str)
        {
            if(CharUnicodeInfo.GetUnicodeCategory(ch)==UnicodeCategory.NonSpacingMark) continue;
            var c=ch is >= ' ' and <= '~' ? ch : '?';
            if(c is '(' or ')' or '\\') buffer.Append('\\');
            buffer.Append(c);
        }
        return buffer.ToString();
    }
}
