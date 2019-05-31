using System;
using System.Buffers;
using System.IO;
using System.Threading.Tasks;
using Nerdbank.Streams;

public class Program
{
    public static async Task Main()
    {
        #region methods
        var seq = new Sequence<char>();

        var mem1 = seq.GetMemory(3);
        mem1.Span[0] = 'a';
        mem1.Span[1] = 'b';
        seq.Advance(2);

        var mem2 = seq.GetMemory(2);
        mem2.Span[0] = 'c';
        mem2.Span[1] = 'd';
        seq.Advance(2);

        ReadOnlySequence<char> ros = seq.AsReadOnlySequence;
        Console.WriteLine(ros.ToArray());

        seq.AdvanceTo(ros.GetPosition(1));
        ros = seq.AsReadOnlySequence;
        Console.WriteLine(ros.ToArray());
        #endregion
    }
}
